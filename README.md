<img src="catalog/artwork/avitex-wordmark-blue.svg" alt="Avitex" width="220">

# Automatic Geometry (legacy bundle)

Automatic Geometry has been split into two independently maintained mods:

- [Component Auto Width](https://github.com/joshua-small/qsys-component-auto-width): fits exposed control-pin labels without changing their text.
- [Compact Connectors](https://github.com/joshua-small/qsys-compact-connectors): 10-unit Container/Channel Group connectors, native pin snapping and open-drawer body snap targets.

Use Mod Manager to replace Automatic Geometry with either or both successors in one profile transaction, together with Mods Core 0.1.7. Their catalog conflicts prevent overlapping legacy hooks. Historical 0.1.x releases, original package names and exact pins remain available for rollback. This repository retains the reviewed split's source history; ongoing successor changes belong in the repositories above. There is no new Automatic Geometry 0.2 package.

The dedicated status menu is removed. Initialization state, assembly identity and failures are in **File > Preferences > Mod Diagnostics**. Ready means initialization succeeded.

[Better Packing](https://github.com/joshua-small/qsys-better-packing) 0.2.2 supplies Pack to Top Pins and Pack to Bottom Pins using actual rendered centers. Boundary packing retains its original meaning.

## Signal Snake research

The owner confirmed on 2026-10-16 that ordinary Signal Snakes work inside Containers, withdrawing the earlier failure report. No fix to normal individual-channel wiring is required for this release. The separate [whole-bundle prototype](SnakeConnectors/README.md) remains future work by explicit owner choice; it is not released or installed. [Tracking #6](https://github.com/joshua-small/qsys-automatic-geometry/issues/6).

## Retained verification

The split's combined synthetic harness has 44 WPF checks and six actual-host metadata/IL checks. Successors retain independent tests. A disposable Designer 10.5 run exercised pin alignment, native snap targets and undo/redo in Containers, Channel Groups and a second nesting level, with the original fixture unchanged. Individual feature repositories document current acceptance and remaining limits.

Build source with .NET 10.0.112, a sibling Mods Core checkout and local DesignerDir references. Vendor binaries, designs, raw captures and build outputs are excluded. Historical source is not an authorization to publish a package with incompatible catalog identity or duplicate successor hooks.
