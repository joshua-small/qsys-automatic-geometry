using System.Collections;
using System.Reflection;
using System.Windows;
using HarmonyLib;
using QsysMods.Core;

namespace Designer.Modules.CompactConnectors;

public sealed class CompactConnectorsModule : ModModule
{
    public CompactConnectorsModule() => Mods.Register("compact-connectors", Initialize);
    private void Initialize(Window main)
    {
        var host = Host.Type("QSC.QSys.Graphic.ContainerPin").Assembly;
        var bindings = new ConnectorBindings(host);
        // Resolve/preflight snapping before any geometry patches become active.
        var snapping = new ConnectorSnapping(host);
        ConnectorHooks.Report = (kind, detail) => Host.Receipt("compact-connectors", kind, detail);
        var harmony = ConnectorHooks.Install(bindings);
        try { snapping.Install(harmony); }
        catch { harmony.UnpatchAll(ConnectorHooks.PatchId); ConnectorHooks.Host = null; throw; }
        Host.Receipt("compact-connectors", "installed", new { module = GetType().Assembly.FullName,
            patched = harmony.GetPatchedMethods().Select(m => m.DeclaringType!.FullName + "." + m.Name).ToArray() });
        try
        {
            var maps = host.GetType("QSC.QSys.Graphic.Maps", true)!;
            var map = maps.GetField("Graphics", Host.Static)!.GetValue(null)!;
            var dictionary = map.GetType().GetField("_map", Host.Instance)!.GetValue(map)!;
            var values = ((IEnumerable)dictionary.GetType().GetProperty("Values")!.GetValue(dictionary)!).Cast<object>().ToArray();
            var connectorType = host.GetType("QSC.QSys.Graphic.ContainerPin", true)!;
            foreach (var graphic in values.Where(connectorType.IsInstanceOfType))
                bindings.GraphicProvider.GetMethod("CreatePins", Host.Instance)!.Invoke(graphic, null);
        }
        catch (Exception error) { Host.Error("compact-connectors", error); }
    }
}
