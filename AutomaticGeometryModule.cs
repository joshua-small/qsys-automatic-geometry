using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using QsysMods.Core;

namespace Designer.Modules.AutomaticGeometry;

// Keep the installed assembly/package identity; the display name is Component Auto Width.
public sealed class AutomaticGeometryModule : ModModule
{
    internal const string DesignerHash = Host.DesignerHash;
    public AutomaticGeometryModule() => Mods.Register("automatic-geometry", Initialize);
    private void Initialize(Window main)
    {
        var host = Host.Type("QSC.QSys.Graphic.PinProvider").Assembly;
        var bindings = new HostBindings(host);
        GeometryHooks.Report = SaveReceipt;
        var harmony = GeometryHooks.Install(bindings);
        SaveReceipt("installed", new { module = GetType().Assembly.FullName, designerHash = DesignerHash,
            patched = harmony.GetPatchedMethods().Select(m => m.DeclaringType!.FullName + "." + m.Name).ToArray() });
        RefreshExistingGraphics(host, bindings);
    }

    private static void RefreshExistingGraphics(Assembly host, HostBindings bindings)
    {
        try
        {
            object map = host.GetType("QSC.QSys.Graphic.Maps", true)!.GetField("Graphics", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            object dictionary = map.GetType().GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(map)!;
            var values = ((IEnumerable)dictionary.GetType().GetProperty("Values")!.GetValue(dictionary)!).Cast<object>().ToArray();
            var refresh = bindings.GraphicProvider.GetMethod("CreatePins", BindingFlags.Public | BindingFlags.Instance)!;
            int count = 0;
            foreach (object graphic in values.Where(bindings.GraphicProvider.IsInstanceOfType))
            {
                refresh.Invoke(graphic, null);
                count++;
            }
            SaveReceipt("existing-graphics-refreshed", new { count });
        }
        catch (Exception error) { SaveReceipt("refresh-error", new { error = error.ToString() }); }
    }

    internal static void SaveReceipt(string kind, object detail)
    {
        try
        {
            string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QsysSchematicTools", "Designer-10.5", "automatic-geometry");
            Directory.CreateDirectory(output);
            string file = Path.Combine(output, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + kind + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(new { kind, utc = DateTime.UtcNow, pid = Environment.ProcessId,
                applicationDirectory = AppContext.BaseDirectory, detail }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Diagnostic I/O must never interrupt Designer. */ }
    }
}
