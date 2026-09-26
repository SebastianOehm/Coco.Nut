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
* Validate the NUT host format (IP address / host name) in the settings, as WinNUT did.
* Windows: check whether hibernation is available before offering it.
* Update check: point it at the repository that publishes the releases (currently `nutdotnet/Coco.Nut`).
* Remove the unused string `Main_FeatureNotImplemented`.
* Verify the WinNUT settings import against a real WinNUT 2.x `user.config` with saved credentials.
* Manual tests with real NUT servers and UPS hardware on Windows, Linux (incl. GNOME tray) and macOS, including a
  real suspend, hibernate and shutdown.
* Native-speaker review of the translations.
* Installer and signed builds; later: automatic update download, UPS commands (`INSTCMD`, `SET VAR`), TLS
  (`STARTTLS`).

## Change log

### 2026-09-26 – Milestone 1
* C# / .NET 10 / Avalonia port of WinNUT-Client 2.3 with feature parity: monitoring, gauges, tray icon,
  notifications, shutdown/suspend/hibernate on low battery, low runtime, FSD and OB+LB, settings, WinNUT settings
  import, UPS variables window, 7 languages (resx).
* Light/dark theme setting; About window with credits and an AI usage note.
* CI on Windows, Linux and macOS; every push to `preMain` publishes a GitHub pre-release.
