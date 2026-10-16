using QsysMods.Core;

namespace Designer.Modules.SnakeConnectors;

// Separate experimental module: generic Containers only, never typed Channel Groups.
public sealed class SnakeConnectorsModule : ModModule
{
    public SnakeConnectorsModule() => Mods.Register("snake-connectors", _ =>
    {
        var hooks = new SnakeHooks(Host.Type("QSC.QSys.Tools.WireTool").Assembly);
        hooks.Report = error => Host.Error("snake-connectors", error);
        hooks.Install();
        Host.Receipt("snake-connectors", "installed", new { module = GetType().Assembly.FullName,
            scope = "generic Container bundles only; Channel Groups remain native" });
    });
}
