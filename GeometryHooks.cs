using System.Collections;
using System.Reflection;
using System.Windows.Media;
using HarmonyLib;

namespace Designer.Modules.AutomaticGeometry;

internal sealed class HostBindings
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal readonly Type GraphicProvider;
    internal readonly FieldInfo LabelText, NominalWidth, UserCreated, PrettyName, CodeName;
    internal readonly PropertyInfo ProviderModel, LabelPpd, Pins;
    internal readonly MethodInfo Measure, CreatePins;
    internal HostBindings(Assembly host)
    {
        Type T(string name) => host.GetType("QSC.QSys." + name, true)!;
        FieldInfo F(Type type, string name) => type.GetField(name, Instance) ?? throw new MissingFieldException(type.FullName, name);
        PropertyInfo P(Type type, string name) => type.GetProperty(name, Instance) ?? throw new MissingMemberException(type.FullName, name);
        GraphicProvider = T("Graphic.PinProvider");
        var label = T("Graphic.ComponentLabelText"); var pin = T("Design.Pin");
        LabelText = F(GraphicProvider, "_LabelText"); NominalWidth = F(label, "NominalWidth");
        ProviderModel = P(GraphicProvider, "InnerPinProvider"); LabelPpd = P(label, "PPD");
        Pins = P(T("Design.PinProvider"), "Pins");
        UserCreated = F(pin, "UserCreated"); PrettyName = F(pin, "PrettyName"); CodeName = F(pin, "CodeName");
        Measure = T("Graphic.Element").GetMethod("GetFormattedText", BindingFlags.Public | BindingFlags.Static,
            null, [typeof(string), typeof(double), typeof(double)], null) ?? throw new MissingMethodException("GetFormattedText");
        CreatePins = GraphicProvider.GetMethod("_CreatePins", Instance | BindingFlags.DeclaredOnly) ?? throw new MissingMethodException("_CreatePins");
        if (NominalWidth.FieldType != typeof(double)) throw new InvalidOperationException("Unexpected label width field.");
    }
}

internal static class GeometryHooks
{
    internal const string PatchId = "qsys.mods.component-auto-width";
    internal static HostBindings? Host;
    internal static Action<string, object>? Report;
    private static int widthReports, failures;
    internal static Harmony Install(HostBindings host)
    {
        if (Harmony.GetPatchInfo(host.CreatePins)?.Owners.Any() == true)
            throw new InvalidOperationException("Component width is already patched; remove the legacy Automatic Geometry bundle.");
        Host = host;
        var harmony = new Harmony(PatchId);
        try { harmony.Patch(host.CreatePins, prefix: new HarmonyMethod(typeof(GeometryHooks), nameof(FitPinLabels))); return harmony; }
        catch { harmony.UnpatchAll(PatchId); Host = null; throw; }
    }
    internal static void FitPinLabels(object __instance)
    {
        try
        {
            var h = Host!;
            object model = h.ProviderModel.GetValue(__instance)!;
            object label = h.LabelText.GetValue(__instance)!;
            double ppd = (double)h.LabelPpd.GetValue(label)!;
            var widths = new List<double>();
            foreach (object pin in (IEnumerable)h.Pins.GetValue(model)!)
            {
                if (!(bool)h.UserCreated.GetValue(pin)!) continue;
                string text = (string?)h.PrettyName.GetValue(pin) ?? "";
                if (text.Length == 0) text = (string?)h.CodeName.GetValue(pin) ?? "";
                // Invoke the host's exact pin-label formatter, preserving font, DPI, and culture.
                var measured = (FormattedText)h.Measure.Invoke(null, [text, 10.0, ppd])!;
                widths.Add(measured.WidthIncludingTrailingWhitespace);
            }
            double target = GeometryRules.TextWidth(widths);
            double previous = (double)h.NominalWidth.GetValue(label)!;
            if (target == previous) return;
            h.NominalWidth.SetValue(label, target);
            // NominalWidth is a field; setting PPD to its existing value invalidates the host cache.
            h.LabelPpd.SetValue(label, h.LabelPpd.GetValue(label));
            if (widthReports++ < 20) Report?.Invoke("width-adjusted", new { previous, target, exposedPinCount = widths.Count });
        }
        catch (Exception error) { Failure("width", error); }
    }

    private static void Failure(string operation, Exception error)
    {
        if (failures++ < 10) Report?.Invoke("geometry-error", new { operation, error = error.GetBaseException().Message });
    }
}
