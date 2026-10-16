# Snake Connectors — experimental

Independent candidate for passing a Signal Snake bundle through a generic Container connector. Container support is the priority. Channel Group bundles are excluded: their typed Audio/Control connectors replicate individual channels and require a separate routing contract.

## Two separate findings

The owner confirmed on 2026-10-16 that ordinary Signal Snakes work inside Containers and withdrew the earlier failure report. They explicitly chose to keep whole-bundle routing as future work while installing the geometry updates. No individual-channel fix is required or claimed. Sixteen isolated native-model compatibility checks also accepted the scalar pairs; they were not a real-canvas reproduction.

Whole-bundle wiring has a concrete restriction: native `WireTool.CanWireMux` requires both endpoints to be Snake providers. Dynamic typing also resets an empty Container boundary to Unknown. This candidate adapts those checks and reuses native connected-pin traversal, per-channel compatibility, network validation and type propagation. It creates no custom wire format and preserves normal endpoint direction and named-input rules.

## Bounded candidate behavior

- Generic Container outer pins and soft-typed internal connectors only; exposed controls and Channel Groups are excluded.
- One bundle source and sink, equal contiguous channel counts (1–256), no new loops or fanout.
- Audio, Control and untyped channels only. Special domains including MediaCast are excluded at bundle connection and when later connecting an individual channel through an established boundary bundle.
- Existing scalar-typed boundaries cannot become bundle connectors.
- Ordinary Snake-to-Snake wiring remains native. This bundle prototype does not replace individual-channel behavior inside Containers.
- Hooks are version guarded, reject competing owners and roll back together on initialization failure.

The type-origin hook follows the connected bundle graph; disconnected generic boundaries return to native Unknown typing. Attached-object propagation, undo/redo, save/reopen, copy/paste, extraction/compile and signal operation need application acceptance. Do not release this prototype as supported routing.

## Evidence and checks

Official [Signal Snakes help](https://help.qsys.com/Content/Schematic_Library/signal_snakes.htm) describes bundle and individual-channel behavior. Private observations here apply to Designer 10.5.0.0, SHA-256 `0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60`, not a public API. Vendor code and binaries stay local.

```text
dotnet build SnakeConnectors/Designer.Modules.SnakeConnectors.csproj -c Release -p:DesignerDir="<Designer directory>" --artifacts-path ../build-snake
dotnet run --project SnakeConnectors/tests/Snake.Tests.csproj -c Release --artifacts-path ../snake-tests
```

Run on Windows. The synthetic Harmony harness passes 20 checks. A Designer directory argument verifies bindings without patches. Adding `--models` runs 16 native individual-channel checks before hooks, then six bundle checks against unattached native objects in the test process. Those confirm rejection before hooks, acceptance after hooks, traversal through a Container jumper and matching-channel extraction. They do not launch or patch a running Designer process or load/save a design.

Future work must validate attached bundle propagation, disconnect/deletion, undo/redo, copy/paste, save/reopen, extraction/compile and signal operation in a small disposable Container fixture before a routing release. Channel Group bundle semantics remain separate. No catalog onboarding, release or installation is part of the geometry update. Tracking: https://github.com/joshua-small/qsys-automatic-geometry/issues/6.
