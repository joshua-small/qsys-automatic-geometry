<img src="catalog/artwork/avitex-wordmark-blue.svg" alt="Avitex" width="220">

# Component Auto Width

Automatically fit component widths to exposed control-pin labels without changing their text.

The unreleased 0.2 source separates the former Automatic Geometry features:

| Module | Behavior |
| --- | --- |
| Component Auto Width | Component label fitting. Keeps the existing `automatic-geometry` package ID and `Designer.Modules.AutomaticGeometry.dll` identity. |
| [Compact Connectors](CompactConnectors/README.md) | Independent 10-unit Container/Channel Group connectors, pin snapping and open-drawer body snap targets. |
| [Snake Connectors](SnakeConnectors/README.md) | Separate experimental Container bundle routing. The reported individual-channel failure inside Containers remains under investigation. |

Each module can run independently with [Mods Core](https://github.com/joshua-small/qsys-mods-core). There is no Automatic Geometry Status menu action. The accompanying Core change puts initialization state, assembly identity and failures in **File > Preferences > Mod Diagnostics**. Ready means initialization succeeded; it does not establish acceptance of every behavior.

Width receipts retain `%LOCALAPPDATA%\QsysSchematicTools\Designer-10.5\automatic-geometry`. Shared status and the other modules use `%LOCALAPPDATA%\QsysMods\Designer-10.5\diagnostics`.

## Candidate and migration

These are reviewable source candidates, not released or installed packages. Published 0.1.x Automatic Geometry packages still combine width and connector resizing. A coordinated release must replace that legacy DLL with the width-only build before enabling Compact Connectors. Competing connector implementations are unsupported; hook-owner preflight rejects existing patches.

All three candidates require Core >=0.1.6 and <0.2.0; the diagnostics page needs the accompanying unreleased Core change. Ship Core as a package dependency, not a private runtime copy. New module identities, renamed presentation and dependency/file-layout changes need signed catalog approval before publication. Historical assets and exact pins remain immutable. The subprojects share this repository for review but produce separate DLLs; they are not automatically included in the width package.

The publishing descriptor retains the approved Automatic Geometry canonical name until a coordinated rename can preserve old package pins and receipts. Component Auto Width is the approved name for this successor; the manager-visible rename is pending that migration, not silently applied to historical releases.

## Development and verification

Clone `qsys-mods-core` beside this repository. Use .NET 10.0.112, local Designer references and separate WSL build outputs:

```text
dotnet build Designer.Modules.AutomaticGeometry.csproj -c Release -p:DesignerDir="<Designer directory>" --artifacts-path ../build-width
dotnet build CompactConnectors/Designer.Modules.CompactConnectors.csproj -c Release -p:DesignerDir="<Designer directory>" --artifacts-path ../build-connectors
dotnet run --project tests/GeometryTests.csproj -c Release --artifacts-path ../geometry-tests
```

Run the WPF harness on Windows. Its 44 checks cover independent hook ownership, fitting, connector geometry, snapping and rollback. Passing the installed Designer directory as an argument adds six metadata/IL checks without invoking or patching host methods. The supported Designer 10.5.0.0 hash is `0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60`; Core also checks ModuleCommon. Other builds stay disabled.

The split, drag snapping, open-drawer behavior and Preferences integration still need scoped Designer checks. `tests/LabProbe` and `tests/ConnectorProbe` retain guarded disposable-Designer fixtures; the connector probe now waits for Compact Connectors. Previous combined-module acceptance does not validate these candidates. No design or application installation was modified by these checks.

Manager presentation and immutable release history live in [catalog/](catalog/README.md).
