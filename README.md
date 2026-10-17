# Coco.Nut

[![Build](https://github.com/SebastianOehm/Coco.Nut/actions/workflows/build.yml/badge.svg)](https://github.com/SebastianOehm/Coco.Nut/actions/workflows/build.yml)

Coco.Nut is a cross-platform desktop client for [Network UPS Tools](https://networkupstools.org/) (NUT), and the
successor of [WinNUT-Client](https://github.com/nutdotnet/WinNUT-Client): a full C#/.NET rewrite that replaces
WinNUT's VB.NET/WinForms/.NET Framework stack (Windows-only) with C# 13, .NET 10 and
[Avalonia UI](https://avaloniaui.net/), so the same client now runs on Windows, Linux and macOS. See
[nutdotnet/WinNUT-Client#40](https://github.com/nutdotnet/WinNUT-Client/issues/40) for the background on why this
rewrite exists, and [`docs/PLAN.md`](docs/PLAN.md) for the detailed migration plan and architecture.

## Status

**Work in progress, pre-release.** Core UPS monitoring, the shutdown/suspend logic, settings, localization and the
platform layer are implemented and tested; the Avalonia UI (windows beyond the main dashboard) is still being built
out. Expect rough edges and missing features until the first tagged release.

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for planned features (data history, multiple NUT servers/UPSes, an actions
system) and the change log.

## Features

- Live monitoring of UPS values (input/output voltage, load, frequency, battery voltage, battery charge, estimated
  runtime) on gauge and bar controls
- Desktop notifications on power/status changes (power lost/restored, low battery, forced shutdown, connection
  lost/restored)
- Configurable shutdown, suspend, or hibernate of the local machine when:
  - the battery charge falls to or below a configured floor while on battery
  - the estimated runtime falls to or below a configured floor while on battery
  - the NUT server requests a forced shutdown (`ups.status` contains `FSD`)
  - the UPS reports its own critical low-battery condition (`ups.status` contains both `OB` and `LB`)
- Automatic reconnection to the NUT server with backoff, so a restart of `upsd` or a network blip does not require
  restarting Coco.Nut
- System tray icon that reflects connection and battery state, with the same icon set WinNUT used
- 7 languages: English, German, French, Russian, Ukrainian, Simplified Chinese, Traditional Chinese
- Runs on Windows, Linux and macOS

## Downloads and releases

- **Pre-releases:** every push to the `preMain` branch builds and tests Coco.Nut and publishes a GitHub
  **pre-release** (`v<version>-pre.<run number>`) with self-contained builds for Windows (x64), Linux (x64) and
  macOS (Apple Silicon) as zip files. These are for testing: try them with `COCONUT_DRY_RUN=1` first.
- **CI builds:** pushes to `main` and `v*` tags also produce the same builds as workflow artifacts.

The base version comes from `<Version>` in `Directory.Build.props`.

## Building and running from source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/) (the exact version is pinned in
[`global.json`](global.json)).

```sh
git clone https://github.com/SebastianOehm/Coco.Nut.git
cd Coco.Nut
dotnet run --project src/CocoNut.App
```

### Running the tests

```sh
dotnet test
```

The build treats warnings as errors, so `dotnet build -c Release` (used in CI) fails on any warning.

### Safe testing: dry-run mode

Set `COCONUT_DRY_RUN=1` to disable real shutdown/suspend/hibernate actions - the action that *would* have run is
only logged, so you can exercise the shutdown logic (e.g. against a fake or throttled UPS) without actually turning
your machine off:

```sh
COCONUT_DRY_RUN=1 dotnet run --project src/CocoNut.App
```

### Custom data directory

By default Coco.Nut stores its settings and logs in the per-user application-data folder for the current OS:

| OS | Location |
|---|---|
| Windows | `%APPDATA%\Coco.Nut` |
| Linux | `~/.config/Coco.Nut` |
| macOS | `~/.config/Coco.Nut` (via `Environment.SpecialFolder.ApplicationData`) |

Inside that folder: `settings.json` (with the NUT password protected via DPAPI on Windows or an AES key file with
restricted permissions elsewhere) and a `logs/` folder with the file logs.

Set `COCONUT_DATA_DIR` to use a different folder instead - useful for portable installs, or to keep tests and
throwaway runs from touching your real settings:

```sh
COCONUT_DATA_DIR=/tmp/coconut-test dotnet run --project src/CocoNut.App
```

## Connecting to a NUT server

Coco.Nut talks to any [NUT](https://networkupstools.org/) server (`upsd`) over the network - the same server your
Linux box, NAS, or dedicated NUT host already runs.

### Synology NAS

If your UPS is attached to a Synology NAS, see the
[Synology documentation](https://kb.synology.com/en-us/DSM/help/DSM/AdminCenter/system_hardware_ups?version=7)
first: you must add your client computer's IP address to the *Permitted DiskStation Devices* list. Then connect
with:

- **Login**: `upsmon`
- **Password**: `secret`
- **UPS name**: `ups`

(See [WinNUT-Client#47](https://github.com/gawindx/WinNUT-Client/issues/47#issuecomment-759180793) for background.)

### QNAP NAS

If your NUT server is hosted on a QNAP NAS, use:

- **UPS name**: `qnapups`
- Login and password can be left empty

Also enable the "Network UPS master" option under Control Panel → External Device on the QNAP web interface, and add
Coco.Nut's client IP address there so it is allowed to connect.

## Migrating from WinNUT

On Windows, Coco.Nut can import settings from an existing WinNUT-Client 2.x installation (connection details,
calibration, power actions, etc.) from its `user.config` file, so upgrading users keep their preferences. WinNUT
1.x's registry/INI-based settings are not supported. WinNUT was Windows-only, so this import is a Windows-only
feature.

## Project structure

```
src/
├─ CocoNut.Core/          NUT protocol client, UPS monitor/calculators, shutdown policy, settings, logging, updates
├─ CocoNut.Localization/  Strings.resx (English) + Strings.<culture>.resx translations
├─ CocoNut.Platform/      Windows/Linux/macOS power actions and autostart implementations
└─ CocoNut.App/           Avalonia desktop app (views, view models, gauge control, tray icon)
tests/                     xUnit tests for CocoNut.Core and CocoNut.App
tools/TranslationImport/   one-shot importer that carries WinNUT's community translations into Strings.*.resx
```

See [`docs/PLAN.md`](docs/PLAN.md) for the full architecture and migration rationale.

## Contributing

Contributions are welcome. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the development setup, coding conventions,
and - importantly - how to add or fix a translation or add a new language.

## Third-party acknowledgements

Coco.Nut is built with:

- [Avalonia UI](https://avaloniaui.net/) ([MIT license](https://opensource.org/licenses/MIT))
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) ([MIT license](https://opensource.org/licenses/MIT))
- [Microsoft.Extensions.*](https://github.com/dotnet/runtime) libraries (logging, dependency injection; MIT license)

The gauge control (`src/CocoNut.App/Controls/Gauge.cs`) is a new implementation for Avalonia, inspired by the
[AGauge](https://github.com/Code-Artist/AGauge) control (MIT license) that WinNUT used. The tray/window icon set
(`src/CocoNut.App/Assets/Icons/`) is inherited unchanged from WinNUT-Client.

## License

Coco.Nut is free software: you can redistribute it and/or modify it under the terms of the GNU General Public
License as published by the Free Software Foundation, either version 3 of the License, or any later version. See
[`LICENSE`](LICENSE) for the full text.

- Coco.Nut: Copyright (C) 2026 Sebastian Oehm, gbakeman
- Based on WinNUT-Client: Copyright (C) 2019-2021 Gawindx (Decaux Nicolas), Copyright (C) 2022+ gbakeman / NUT Dot Net project

WinNUT-Client and the [nutdotnet](https://github.com/nutdotnet) library are maintained by gbakeman and the NUT Dot Net
project.

## Use of AI

Large parts of Coco.Nut were written with AI assistance (Claude by Anthropic, using Claude Code): an AI model planned
and reviewed the work, and AI sub-agents wrote most of the code. All changes were directed and checked by the
maintainers and are covered by automated builds and tests (see the CI badge above). Please report problems as
GitHub issues.
