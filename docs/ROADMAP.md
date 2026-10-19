# Coco.Nut – Roadmap and change log

This file tracks where Coco.Nut is heading after milestone 1 (the C# port of WinNUT-Client, see
[`PLAN.md`](PLAN.md)) and records notable changes as they happen. Update it with every larger change.

## Original goals (by gbakeman)

Coco.Nut was proposed by gbakeman, maintainer of WinNUT-Client and the nutdotnet library, as the successor of
WinNUT ([nutdotnet/WinNUT-Client#40](https://github.com/nutdotnet/WinNUT-Client/issues/40)). His goals for it:

| # | Goal | Status |
|---|------|--------|
| G1 | Cross-platform, native desktop app written in C# and XAML | **Done** (milestone 1). Built with Avalonia UI instead of the originally mentioned .NET MAUI: Avalonia supports Linux desktops, MAUI does not. |
| G2 | Keep a history of the data points collected from a UPS | Open |
| G3 | Monitor multiple NUT servers at once, with multiple UPSes attached to each | Open |
| G4 | Actions system: the user defines actions that run when certain conditions are met; default actions where supported (hibernate or shut down the system, run commands, notify, …) | Partly done: fixed rules (charge/runtime floor, FSD, OB+LB) with shutdown/suspend/hibernate and notifications |

## Planned work

### G2 – Data history
* Record every `UpsReading` (timestamp, status, charge, runtime, voltages, frequency, load, power) per UPS.
* Storage: a local SQLite database in the app data directory, with a retention setting (e.g. 30 days) and
  downsampling of old data.
* UI: history charts (charge, load, input voltage, power) with selectable time range; export to CSV.
* Event history: power failures, shutdowns and connection losses as a separate, searchable list.

### G3 – Multiple servers and UPSes
* Settings model: a list of servers (host, port, credentials, auto-reconnect), each with a list of UPS names;
  `LIST UPS` offers the UPSes available on a server. The current single-connection settings migrate to one entry.
* Core: one `NutClient` per server shared by one `UpsMonitor` per UPS (the protocol allows several `LOGIN`s per
  connection), instead of the current single monitor.
* UI: an overview with one card per UPS (status, charge, runtime) and the current gauge view as a detail page;
  the tray icon shows the worst state of all UPSes.
* Shutdown: decide per UPS whether it powers this computer (only those can trigger the local stop action).

### G4 – Actions system
* Generalise `ShutdownPolicy` into rules: **trigger** (status flag becomes active/inactive, value below/above a
  threshold, connection lost for N seconds, time on battery ≥ N) + optional delay/countdown + **actions**.
* Built-in actions: shut down / suspend / hibernate this computer, notification, run a command or script (with
  UPS values as environment variables), send an e-mail/webhook, write to the log.
* The current fixed stop conditions become the default rule set, so existing settings keep working.
* Safety: actions that power the machine down keep the countdown window, the dry-run mode and the re-arm delay.

## Backlog (from milestone 1)
* Verify the WinNUT settings import against a real WinNUT 2.x `user.config` with saved credentials.
* Manual tests with real NUT servers and UPS hardware on Windows, Linux (incl. GNOME tray) and macOS, including a
  real suspend, hibernate and shutdown.
* Native-speaker review of the translations.
* Signed builds (Windows code signing, macOS signing/notarization) and verifying the macOS pkg end to end; later:
  UPS commands (`INSTCMD`, `SET VAR`), TLS (`STARTTLS`).

## Change log

### 2026-10-19 – Milestone 1 backlog cleanup
* The NUT host in the settings is validated like in WinNUT: an IPv4/IPv6 address (brackets optional) or a DNS host
  name; a protocol, port, path or spaces show "Enter a valid IP address or host name" (new string, all languages).
* Windows: hibernate is only offered when `IsPwrHibernateAllowed` (powrprof.dll) says it is available; if the call
  is not possible it still reports "supported".
* Linux AppImage and macOS packages get an app icon (`coconut.png`, `coconut.icns` next to `coconut.ico`).
* Removed the unused string `Main_FeatureNotImplemented`.

### 2026-10-19 – Installer and in-app updates (Velopack)
* CI packs each platform on its own runner with Velopack: `CocoNut-win-Setup.exe` (per-user install), a Linux
  AppImage and a macOS pkg, plus portable zips; every `preMain` pre-release carries the full Velopack file set
  (delta packages when a previous release exists).
* The update window has a **Download and install** button for installed builds: progress bar, restart into the new
  version, error text with **Open release page** as fallback. Refused while a shutdown countdown is active.
  Portable and dev runs behave as before.
* `VelopackApp.Build().Run()` runs first in `Main`; the updater follows the Stable/Pre-release channel setting.
* Start with system on Linux AppImage records the `.AppImage` path (`APPIMAGE`) instead of the temporary mount path.

### 2026-10-19 – Update check for preMain builds
* The update check now queries `SebastianOehm/Coco.Nut` (previously the empty `nutdotnet/Coco.Nut`).
* Versions are compared SemVer-style including the pre-release label, so a running `0.1.0-pre.12` build detects
  `0.1.0-pre.13`; the running version comes from the assembly's informational version, and the update dialog and
  notifications show the full text (e.g. `0.1.0-pre.13`).
* The About window and crash reports show the same version text (e.g. `0.1.0-pre.12`, no `+commit`).

### 2026-09-26 – Milestone 1
* C# / .NET 10 / Avalonia port of WinNUT-Client 2.3 with feature parity: monitoring, gauges, tray icon,
  notifications, shutdown/suspend/hibernate on low battery, low runtime, FSD and OB+LB, settings, WinNUT settings
  import, UPS variables window, 7 languages (resx).
* Light/dark theme setting; About window with credits and an AI usage note.
* CI on Windows, Linux and macOS; every push to `preMain` publishes a GitHub pre-release.
