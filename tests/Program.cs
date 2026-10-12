using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Media;
using Designer.Modules.AutomaticGeometry;
using HarmonyLib;

internal static class Program
{
    private static int checks;
    private static IEnumerable<GlyphRunDrawing> Glyphs(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (Drawing child in group.Children)
                foreach (var item in Glyphs(child)) yield return item;
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            string directory = args[0];
            AssemblyLoadContext.Default.Resolving += (_, name) =>
                File.Exists(Path.Combine(directory, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "Q-Sys Designer.dll"));
            var actual = new HostBindings(assembly);
            Check(actual.CreatePins.MetadataToken == 0x06002efd, "actual QDS fit method token");
            Check(GeometryHooks.TenUnitCreation(PatchProcessor.GetOriginalInstructions(actual.ContainerCreate)).Count() > 0, "actual Container helper has exactly one height/pitch constant");
            Check(GeometryHooks.TenUnitCreation(PatchProcessor.GetOriginalInstructions(actual.ChannelCreate)).Count() > 0, "actual Channel Group helper has exactly one height/pitch constant");
            Check(GeometryHooks.CenterConnectorPins(PatchProcessor.GetOriginalInstructions(actual.ConnectorCreatePins)).Count() > 0, "actual connector has exactly two pin-center constructions");
            Check(GeometryHooks.CenterConnectorText(PatchProcessor.GetOriginalInstructions(actual.ConnectorRedraw)).Count() > 0, "actual connector has exactly two text drawing calls");
            Console.WriteLine($"{checks} actual-host metadata checks passed; no host method invoked or patched.");
            return 0;
        }
        var events = new List<string>();
        GeometryHooks.Report = (kind, _) => events.Add(kind);
        var h = GeometryHooks.Install(new HostBindings(Assembly.GetExecutingAssembly()));
        Check(h.GetPatchedMethods().Count() == 6, "six targeted methods hooked on .NET 10");
        var model = new QSC.QSys.Design.PinProvider { Label = "Name\u001dLabel\u001d" };
        var graphic = new QSC.QSys.Graphic.PinProvider(model);
        graphic._CreatePins();
        Check(graphic.Calls == 1 && graphic.TextWidth == 54, "unexposed component retains native minimum and executes once");
        model.Pins.Add(new() { PrettyName = new string('W', 100), UserCreated = false });
        graphic._CreatePins();
        Check(graphic.TextWidth == 54, "built-in pin names do not widen the body");
        string[] names = ["Bypass", "Echo Return Loss Enhancement", "Hold If Microphone Level Below", "Hold If Reference Level Below", "Noise Reduction", "Noise Reduction Enable", "Reference Gain", "Reference-to-Microphone Level Ratio", "Residual Echo Suppression", "Residual Echo Suppression Enable"];
        foreach (string name in names) model.Pins.Add(new() { PrettyName = name, UserCreated = true });
        graphic._CreatePins();
        Check(names.All(name => QSC.QSys.Graphic.Element.GetFormattedText(name, 10, 1).Width < graphic.TextWidth), "every exposed AEC pin label fits");
        Check(model.Label == "Name\u001dLabel\u001d", "label bytes and separators unchanged");
        Check(graphic.LabelText.Invalidations == 1, "minimum-width change invalidates cached text");
        graphic._CreatePins();
        Check(graphic.LabelText.Invalidations == 1, "unchanged geometry does not repeatedly invalidate");
        model.Pins.Clear();
        model.Pins.Add(new() { PrettyName = "", CodeName = "Fallback " + new string('W', 40), UserCreated = true });
        graphic._CreatePins();
        Check(graphic.TextWidth > 300, "empty PrettyName uses CodeName");
        model.Pins.Clear();
        graphic._CreatePins();
        Check(graphic.TextWidth == 54, "removing exposed pins restores native minimum");
        Check(graphic.Calls == 6, "prefix always preserves one original sizing invocation");
        var container = new QSC.QSys.Design.ContainerPin { Size = new Size(47,12), Position = new Point(30,84) };
        container.Attach();
        Check(container.Size == new Size(47,10) && container.Position == new Point(30,84), "old connector normalizes height only during attach");
        Check(container.SetterCalls == 1, "normalization adds no setter or undo action");
        var channel = new QSC.QSys.Design.ChannelGroupPin { Size = new Size(31,12), Position = new Point(600,96) };
        channel.Attach();
        Check(channel.Size.Height == 10 && channel.Position.Y == 96, "Channel Group normalization preserves existing position");
        var fallback = new QSC.QSys.Design.ContainerPin { Size = new Size(30,12) };
        var fallbackGraphic = new QSC.QSys.Graphic.ContainerPin(fallback);
        fallbackGraphic._CreatePins();
        Check(fallback.Size.Height == 10 && fallbackGraphic.Calls == 1, "graphic initialization covers already-attached connectors");
        Check(QSC.QSys.Design.ContainerHelper.Create(3).Position.Y == 30 && QSC.QSys.Design.ContainerHelper.Create(3).Size.Height == 10, "new Container connectors use 10-unit height and pitch");
        Check(QSC.QSys.Design.ChannelGroupHelper.Create(3).Position.Y == 30 && QSC.QSys.Design.ChannelGroupHelper.Create(3).Size.Height == 10, "new Channel Group connectors use 10-unit height and pitch");
        fallback.Pins.Add(new() { Input = true });
        fallback.Pins.Add(new() { Input = false });
        fallback.Pins.Add(new() { Input = false, GraphicIndex = 3 });
        fallbackGraphic._CreatePins();
        Check(fallback.Pins[0].Center == new Point(6,5) && fallback.Pins[1].Center == new Point(24,5), "both pin directions center vertically without changing x");
        Check(fallback.Pins[2].Center.Y == 35, "additional pin indices keep ten-unit pitch");
        fallbackGraphic._CreatePins();
        Check(fallback.Pins[0].Center.Y == 5, "recreating pins does not accumulate offsets");
        foreach (bool input in new[] { false, true })
        {
            fallbackGraphic.Input = input;
            foreach (string number in new[] { "1", "10", "888" })
            {
                fallback.Label = number;
                fallbackGraphic.Redraw();
                var drawing = Glyphs(fallbackGraphic.Visual.Drawing).Single();
                Rect ink = drawing.GlyphRun.BuildGeometry().Bounds;
                double center = ink.Top + ink.Height / 2;
                Check(Math.Abs(center - 5) < 0.0001 && drawing.GlyphRun.FontRenderingEmSize == 10,
                    $"{(input ? "right" : "left")} label {number} ink centered with unchanged size 10");
            }
        }
        fallback.Label = "";
        fallbackGraphic.Redraw();
        Check(fallbackGraphic.Visual.Drawing is null || fallbackGraphic.Visual.Drawing.Children.Count == 0, "empty connector label stays empty");
        Check(!events.Contains("geometry-error"), "no hook failures");
        bool rejected = false;
        try { GeometryHooks.TenUnitCreation([]).ToList(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unexpected creation IL rejected");
        rejected = false;
        try { GeometryHooks.CenterConnectorPins([]).ToList(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unexpected pin-center IL rejected");
        rejected = false;
        try { GeometryHooks.CenterConnectorText([]).ToList(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unexpected text-drawing IL rejected");
        h.UnpatchAll(GeometryHooks.PatchId);
        Check(QSC.QSys.Design.ContainerHelper.Create(3).Size.Height == 12 && QSC.QSys.Design.ChannelGroupHelper.Create(3).Position.Y == 36, "uninstall restores original creation behavior");
        Check(container.Size.Height == 10, "normalized stored model height remains after removing hooks");
        fallbackGraphic._CreatePins();
        Check(fallback.Pins[0].Center.Y == 6, "unpatch restores native pin-center calculation");
        Console.WriteLine($"{checks} behavioral checks passed on native Windows WPF / {Environment.Version}.");
        return 0;
    }
}

namespace QSC.QSys.Design
{
    public class Object
    {
        protected Size _Size = new(82,24);
        public int SetterCalls;
        public Size Size { get => _Size; set { SetterCalls++; _Size = value; } }
        public Point Position { get; set; }
        public string Label { get; set; } = "";
    }
    public class PinProvider : Object { public List<Pin> Pins { get; } = []; }
    public class Pin { public bool UserCreated; public string PrettyName = ""; public string CodeName = ""; public bool Input; public int GraphicIndex; public Point Center; }
    public class ContainerPin : PinProvider
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void _Attach() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Attach() => _Attach();
    }
    public class ChannelGroupPin : ContainerPin { }
    public static class ContainerHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)] private static ContainerPin CreatePins(int index)
        {
            int pitch = 12;
            return new ContainerPin { Size = new Size(30,pitch), Position = new Point(0,index * pitch) };
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public static ContainerPin Create(int index) => CreatePins(index);
    }
    public static class ChannelGroupHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)] private static ChannelGroupPin CreateInternalPin(int index)
        {
            int pitch = 12;
            return new ChannelGroupPin { Size = new Size(30,pitch), Position = new Point(600,index * pitch) };
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public static ChannelGroupPin Create(int index) => CreateInternalPin(index);
    }
}

namespace QSC.QSys.Graphic
{
    public class ComponentLabelText
    {
        public double NominalWidth = 54;
        private double ppd = 1;
        public int Invalidations;
        public double PPD { get => ppd; set { ppd = value; Invalidations++; } }
    }
    public class PinProvider
    {
        protected ComponentLabelText _LabelText = new();
        public ComponentLabelText LabelText => _LabelText;
        public Design.PinProvider InnerPinProvider { get; }
        public int Calls;
        public double TextWidth;
        public PinProvider(Design.PinProvider model) => InnerPinProvider = model;
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void _CreatePins() { Calls++; TextWidth = _LabelText.NominalWidth; }
    }
    public class ContainerPin(Design.ContainerPin model) : PinProvider(model)
    {
        public DrawingVisual Visual = new();
        public bool Input;
        [MethodImpl(MethodImplOptions.NoInlining)] public override void _CreatePins()
        {
            Calls++;
            foreach (var pin in InnerPinProvider.Pins)
            {
                if (pin.Input) pin.Center = new Point(6, 6 + 10 * pin.GraphicIndex);
                else pin.Center = new Point(InnerPinProvider.Size.Width - 6, 6 + 10 * pin.GraphicIndex);
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Redraw()
        {
            using var context = Visual.RenderOpen();
            if (!string.IsNullOrEmpty(InnerPinProvider.Label))
            {
                var text = Element.GetFormattedText(InnerPinProvider.Label, 10, 1);
                if (Input) context.DrawText(text, new Point(InnerPinProvider.Size.Width - text.Width - 2, 0));
                else context.DrawText(text, new Point(2, 0));
            }
        }
    }
    public static class Element
    {
        public static FormattedText GetFormattedText(string text, double size, double ppd) =>
            new(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Calibri"), size, Brushes.Black, ppd);
    }
}
