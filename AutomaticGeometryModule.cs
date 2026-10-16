using System.IO;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.AutomaticGeometry;

public sealed class AutomaticGeometryModule : IDesignerModule
{
    internal const string DesignerHash = "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60";
    private const string CommonHash = "a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee";
    private DispatcherTimer? startup;
    private int startupAttempts;
    public string Description => "Automatically fit exposed pin labels and use 10-unit internal connectors.";
    public string MenuTitle => "Automatic Geometry";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };

    public AutomaticGeometryModule()
    {
        try
        {
            if (Hash(Path.Combine(AppContext.BaseDirectory, "Q-Sys Designer.dll")) != DesignerHash ||
                Hash(Path.Combine(AppContext.BaseDirectory, "ModuleCommon.dll")) != CommonHash)
                throw new InvalidOperationException("Designer build differs from the inspected 10.5.0 build; no geometry hooks installed.");
            startup = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(250) };
            startup.Tick += (_, _) => StartWhenReady();
            startup.Start();
        }
        catch (Exception error)
        {
            SaveReceipt("startup-error", new { error = error.ToString() });
        }
    }

    private void StartWhenReady()
    {
        try
        {
            if (++startupAttempts > 240) throw new InvalidOperationException("Designer did not become ready within 60 seconds.");
            if (Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true } main) return;
            Assembly host = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "Q-Sys Designer");
            Type app = host.GetType("QSC.QSys.App", true)!;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            object? window = app.GetProperty("Window", flags)?.GetValue(null) ?? app.GetField("Window", flags)?.GetValue(null);
            if (window is null) return;
            if (main.FindName("mainMenu") is not ItemsControl menu) return;
            // The inspected loader always adds a menu item, even for an automatic module.
            // Remove only our own adapter, identified by its DataContext, once menus exist.
            int removed = RemoveLoaderMenu(menu);
            if (removed == 0) return;
            SaveReceipt("menu-removed", new { count = removed });
            startup!.Stop();
            startup = null;
            var bindings = new HostBindings(host);
            GeometryHooks.Report = SaveReceipt;
            var harmony = GeometryHooks.Install(bindings);
            SaveReceipt("installed", new { module = GetType().Assembly.FullName, designerHash = DesignerHash,
                patched = harmony.GetPatchedMethods().Select(x => x.DeclaringType!.FullName + "." + x.Name).ToArray() });
            RefreshExistingGraphics(host, bindings);
        }
        catch (Exception error)
        {
            startup?.Stop();
            startup = null;
            SaveReceipt("startup-error", new { error = error.ToString() });
        }
    }

    private int RemoveLoaderMenu(ItemsControl parent)
    {
        int removed = 0;
        foreach (var item in parent.Items.OfType<MenuItem>().ToArray())
        {
            if (ReferenceEquals(item.DataContext, this))
            {
                parent.Items.Remove(item);
                removed++;
            }
            else removed += RemoveLoaderMenu(item);
        }
        return removed;
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

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
