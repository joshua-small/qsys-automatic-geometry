<img src="catalog/artwork/avitex-wordmark-blue.svg" alt="Avitex" width="220">

# Q-SYS Automatic Geometry

Automatically make components wide enough for their control-pin labels and use 10-unit Container/Channel Group connectors.

Install **Automatic Geometry** from [Q-SYS Mod Manager](https://github.com/joshua-small/qsys-mod-manager/releases/latest), then restart Designer. It works automatically and keeps component label text unchanged.

The module removes its loader-created menu item after Designer startup; it needs no manual action. Startup, compatibility and geometry diagnostics remain in JSON receipts under `%LOCALAPPDATA%\QsysSchematicTools\Designer-10.5\automatic-geometry`. An `installed` receipt records hook registration for that process, not acceptance of every geometry behavior. On an unsupported Designer build the module records a startup error and makes no hooks or menu changes.

The automatic width behavior requires the mod. Existing connector positions are preserved.

Disable or remove the bundle through Mod Manager while Designer is closed. Supports the compatible Q-SYS Designer 10.5 build on Windows; this is an unofficial Designer extension.

The mod's manager description, artwork and version-specific notes live in [catalog/](catalog/README.md). Its release workflow publishes verified metadata to the separate approved catalog. Modern packages use no enable marker; Mod Manager controls activation by managing the module file.
