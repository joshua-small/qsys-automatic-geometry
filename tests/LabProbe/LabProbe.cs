using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.GeometryLabProbe;

// Disposable lab acceptance driver. Never install this assembly in regular Designer.
public sealed class LabProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string Input, string InputHash, string Receipt, string? SaveAs);
    private readonly Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    public string Description => "Isolated automatic geometry acceptance probe";
    public string MenuTitle => "Geometry lab probe (automatic)";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static object P(object o, string name) => o.GetType().GetProperty(name, PublicInstance)!.GetValue(o)!;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public LabProbe()
    {
        string file = Path.Combine(AppContext.BaseDirectory, ".geometry-lab-probe.json");
        if (!File.Exists(file) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(file))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
        if (!Environment.GetCommandLineArgs().Any(x => x.Equals(config.Input, StringComparison.OrdinalIgnoreCase))) return;
        if (Hash(config.Input) != config.InputHash || File.Exists(config.Receipt)) return;
        if (config.SaveAs is not null && (File.Exists(config.SaveAs) || Path.GetDirectoryName(config.SaveAs) != Path.GetDirectoryName(config.Input))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) => await Run();
        timer.Start();
    }

    private async Task Run()
    {
        try
        {
            if (++attempts > 60) throw new InvalidOperationException("Lab document did not become ready.");
            if (Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true } window ||
                !window.Title.StartsWith(Path.GetFileNameWithoutExtension(config!.Input), StringComparison.Ordinal)) return;
            Assembly host = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "Q-Sys Designer");
            Type T(string name) => host.GetType("QSC.QSys." + name, true)!;
            object map = T("Graphic.Maps").GetField("Graphics", Static)!.GetValue(null)!;
            object dictionary = map.GetType().GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(map)!;
            object[] graphics = ((IEnumerable)P(dictionary, "Values")).Cast<object>().Where(T("Graphic.PinProvider").IsInstanceOfType).ToArray();
            if (graphics.Length == 0 || attempts < 10) return;
            timer!.Stop();
            var connection = T("ControlConnection.Manager");
            object connectionStatus = connection.GetProperty("Status", Static)!.GetValue(null)!;
            if ((bool)P(connectionStatus, "IsConnected") || (bool)connection.GetProperty("IsEmulator", Static)!.GetValue(null)!)
                throw new InvalidOperationException("Lab probe requires disconnected Design mode.");
            var measure = T("Graphic.Element").GetMethod("GetFormattedText", BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string), typeof(double), typeof(double)], null)!;
            var labelField = T("Graphic.PinProvider").GetField("_LabelText", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var snapshots = graphics.Select(g =>
            {
                object m = P(g, "InnerPinProvider");
                object label = labelField.GetValue(g)!;
                double ppd = (double)P(label, "PPD");
                var size = (Size)P(g, "Size");
                var stored = (Size)P(m, "Size");
                object[] pins = ((IEnumerable)P(m, "Pins")).Cast<object>().Where(p => (bool)p.GetType().GetField("UserCreated")!.GetValue(p)!).ToArray();
                var widths = pins.Select(p =>
                {
                    string name = (string?)p.GetType().GetField("PrettyName")!.GetValue(p) ?? "";
                    if (name.Length == 0) name = (string?)p.GetType().GetField("CodeName")!.GetValue(p) ?? "";
                    return ((FormattedText)measure.Invoke(null, [name, 10.0, ppd])!).WidthIncludingTrailingWhitespace;
                }).ToArray();
                string labelString = (string)P(m, "Label");
                return new { type = m.GetType().FullName, position = P(m, "Position").ToString(),
                    label = labelString, labelHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(labelString))),
                    width = size.Width, storedWidth = stored.Width, storedHeight = stored.Height,
                    exposedPins = widths.Length, widestPin = widths.DefaultIfEmpty(0).Max(),
                    availablePinWidth = size.Width - 28, allFit = widths.All(w => w <= size.Width - 28) };
            }).ToArray();
            bool? saved = null;
            if (config!.SaveAs is not null)
                saved = await (Task<bool>)T("Commands.Save").GetMethod("SaveFile", Static)!.Invoke(null, [config.SaveAs])!;
            File.WriteAllText(config.Receipt, JsonSerializer.Serialize(new { status = "complete", utc = DateTime.UtcNow,
                pid = Environment.ProcessId, snapshots, saved, savedHash = saved == true ? Hash(config.SaveAs!) : null,
                originalUnchanged = Hash(config.Input) == config.InputHash }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            timer?.Stop();
            if (config is not null) File.WriteAllText(config.Receipt, JsonSerializer.Serialize(new { status = "error", error = error.ToString() }));
        }
    }
}
