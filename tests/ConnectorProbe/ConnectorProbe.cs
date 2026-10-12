using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.ConnectorProbe;

// Offscreen native-graphic acceptance only. No model is attached or added to a design.
public sealed class ConnectorProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string Input, string InputHash, string OutputDirectory);
    private Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static object P(object o, string n) => o.GetType().GetProperty(n, Instance)!.GetValue(o)!;
    private static void Set(object o, string n, object v)
    {
        if (o.GetType().GetProperty(n, Instance) is { } property) property.SetValue(o, v);
        else if (o.GetType().GetField(n, Instance) is { } field) field.SetValue(o, v);
        else throw new MissingMemberException(o.GetType().FullName, n);
    }
    public string Description => "Isolated native connector rendering check";
    public string MenuTitle => "Connector rendering lab probe";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };

    public ConnectorProbe()
    {
        string file = Path.Combine(AppContext.BaseDirectory, ".connector-centering-probe.json");
        if (!File.Exists(file) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(file))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
            !Environment.GetCommandLineArgs().Any(x => x.Equals(config.Input, StringComparison.OrdinalIgnoreCase)) ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config.Input))).ToLowerInvariant() != config.InputHash ||
            Directory.Exists(config.OutputDirectory)) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Run();
        timer.Start();
    }

    private static IEnumerable<GlyphRunDrawing> Glyphs(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (Drawing child in group.Children)
                foreach (var item in Glyphs(child)) yield return item;
    }

    private void Run()
    {
        try
        {
            if (++attempts > 90) throw new InvalidOperationException("Designer did not become ready.");
            if (attempts < 15 || Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true }) return;
            Assembly host = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "Q-Sys Designer");
            Type T(string name) => host.GetType("QSC.QSys." + name, true)!;
            var mod = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "Designer.Modules.AutomaticGeometry");
            if (mod.GetType("Designer.Modules.AutomaticGeometry.GeometryHooks", true)!.GetField("Host", Static)!.GetValue(null) is null) return;
            object map = T("Graphic.Maps").GetField("Graphics", Static)!.GetValue(null)!;
            object dictionary = map.GetType().GetField("_map", Instance)!.GetValue(map)!;
            object? existing = ((IEnumerable)P(dictionary, "Values")).Cast<object>().FirstOrDefault(T("Graphic.PinProvider").IsInstanceOfType);
            if (existing is null) return;
            timer!.Stop();
            object connectionStatus = T("ControlConnection.Manager").GetProperty("Status", Static)!.GetValue(null)!;
            if ((bool)P(connectionStatus, "IsConnected") || (bool)T("ControlConnection.Manager").GetProperty("IsEmulator", Static)!.GetValue(null)!)
                throw new InvalidOperationException("Requires disconnected Design mode.");
            object manager = T("App").GetField("Manager", Static)!.GetValue(null)!;
            bool dirtyBefore = (bool)P(manager, "Dirty");
            object layer = T("Graphic.Object").GetField("_Layer", Instance)!.GetValue(existing)!;
            var outputs = new List<object>();
            var preview = new DrawingVisual();
            using (var drawing = preview.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(40,43,47)), null, new Rect(0,0,380,200));
                int row = 0;
                foreach (string kind in new[] { "ContainerPin", "ChannelGroupPin" })
                foreach (string number in new[] { "1", "10", "888" })
                {
                    int column = 0;
                    foreach (string direction in new[] { "Output", "Input" })
                    {
                        object model = Activator.CreateInstance(T("Design." + kind))!;
                        // These are unattached probe objects. Set their backing
                        // fields so host setters cannot add global undo entries.
                        T("Design.Object").GetField("_Label", Instance)!.SetValue(model, number);
                        T("Design.Object").GetField("_Size", Instance)!.SetValue(model, new Size(36,12));
                        object pin = Activator.CreateInstance(T("Design.Pin"))!;
                        Set(pin, "Provider", model);
                        var directionField = pin.GetType().GetField("Direction")!;
                        directionField.SetValue(pin, Enum.Parse(directionField.FieldType, direction));
                        object pins = P(model, "Pins");
                        pins.GetType().GetMethod("Add", [T("Design.Pin")])!.Invoke(pins, [pin]);
                        var graphic = (DrawingVisual)Activator.CreateInstance(T("Graphic.ContainerPin"), [model, layer])!;
                        T("Graphic.PinProvider").GetMethod("CreatePins")!.Invoke(graphic, null);
                        var glyph = Glyphs(graphic.Drawing).Single().GlyphRun;
                        var ink = glyph.BuildGeometry().Bounds;
                        var center = (Point)P(pin, "Center");
                        var pinGraphic = VisualTreeHelper.GetChild(graphic, 0);
                        var visualCenter = (Point)P(pinGraphic, "Position");
                        double textCenter = ink.Top + ink.Height / 2;
                        bool pass = ((Size)P(model,"Size")).Height == 10 && center.Y == 5 && visualCenter.Y == 5 &&
                            Math.Abs(textCenter - 5) < 0.0001 && glyph.FontRenderingEmSize == 10 && (string)P(model, "Label") == number;
                        outputs.Add(new { kind, number, direction, height = ((Size)P(model,"Size")).Height,
                            pinY = center.Y, visualPinY = visualCenter.Y, textInkCenter = textCenter, fontSize = glyph.FontRenderingEmSize, pass });
                        drawing.PushTransform(new TranslateTransform(8 + column * 190, 6 + row * 32));
                        drawing.PushTransform(new ScaleTransform(3,3));
                        drawing.DrawDrawing(graphic.Drawing);
                        var pinDrawing = ((DrawingVisual)pinGraphic).Drawing;
                        drawing.PushTransform(new TranslateTransform(visualCenter.X, visualCenter.Y));
                        drawing.DrawDrawing(pinDrawing);
                        drawing.Pop(); drawing.Pop(); drawing.Pop();
                        column++;
                    }
                    row++;
                }
            }
            Directory.CreateDirectory(config!.OutputDirectory);
            var bitmap = new RenderTargetBitmap(380,200,96,96,PixelFormats.Pbgra32);
            bitmap.Render(preview);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(config.OutputDirectory, "native-connectors.png"))) png.Save(stream);
            File.WriteAllText(Path.Combine(config.OutputDirectory, "receipt.json"), JsonSerializer.Serialize(new {
                status = "complete", pid = Environment.ProcessId, module = mod.FullName, outputs,
                dirtyBefore, dirtyAfter = P(manager,"Dirty"), originalUnchanged = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config.Input))).ToLowerInvariant() == config.InputHash
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            timer?.Stop();
            Directory.CreateDirectory(config!.OutputDirectory);
            File.WriteAllText(Path.Combine(config.OutputDirectory, "receipt.json"), JsonSerializer.Serialize(new { status = "error", error = error.ToString() }));
        }
    }
}
