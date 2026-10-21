# Coco.Nut – Roadmap and change log

This file tracks where Coco.Nut is heading after milestone 1 (the C# port of WinNUT-Client, see
[`PLAN.md`](PLAN.md)) and records notable changes as they happen. Update it with every larger change.

## Versioning

The base version lives in `<Version>` in [`Directory.Build.props`](../Directory.Build.props). Each completed roadmap
goal raises the minor version (G2 gave 0.2.0, G3 gave 0.3.0, the next completed goal gives 0.4.0); fixes in
between raise the patch version. Every push to `preMain` publishes a pre-release named `<base>-pre.<run number>`,
e.g. `0.3.0-pre.3`.

## Original goals (by gbakeman)

Coco.Nut is a proof of concept for a possible future successor of WinNUT. gbakeman, maintainer of WinNUT-Client
and the nutdotnet library, proposed a C# rewrite in
[nutdotnet/WinNUT-Client#40](https://github.com/nutdotnet/WinNUT-Client/issues/40) and listed these goals for it,
numbered here in the order they ship (goal G<n> ships as 0.<n>.0):

| # | Goal | Status |
|---|------|--------|
| G1 | Cross-platform, native desktop app written in C# and XAML | **Done** (milestone 1). Built with Avalonia UI instead of the originally mentioned .NET MAUI: Avalonia supports Linux desktops, MAUI does not. |
| G2 | Keep a history of the data points collected from a UPS | **Done** (0.2.0). A SQLite history of readings and events with a History window (charts, searchable events, CSV export) and a Settings section. |
| G3 | Actions system: the user defines actions that run when certain conditions are met; default actions where supported (hibernate or shut down the system, run commands, notify, …) | **Done** (0.3.0). A rule engine with triggers, delays and actions (stop the computer, notification, log entry, command, webhook) replaces the fixed shutdown policy; the old conditions are built-in rules and user rules are edited on a Rules tab in the Settings. |
| G4 | Monitor multiple NUT servers at once, with multiple UPSes attached to each | Open |

## Planned work

### G2 – Data history (done in 0.2.0)
Readings and events are recorded per UPS into `history.db`; the History window charts them and lists the events;
both export to CSV. Details in the change log below.

### G3 – Actions system (done in 0.3.0)
A rule engine replaces the fixed shutdown policy: a rule has a trigger, an optional delay and a list of actions, and the
former stop conditions are the built-in rules. User rules are edited in Settings > Rules. Details in the change log
below.

Follow-ups:
* E-mail action (needs protected storage for SMTP credentials).
* Working directory and placeholder expansion in the arguments of the command action.
* A "test action" button in the rule editor.
* Rules per UPS once G4 lands: `ActionRule.UpsName` already exists but is ignored for now.

### G4 – Multiple servers and UPSes
* Settings model: a list of servers (host, port, credentials, auto-reconnect), each with a list of UPS names;
  `LIST UPS` offers the UPSes available on a server. The current single-connection settings migrate to one entry.
* Core: one `NutClient` per server shared by one `UpsMonitor` per UPS (the protocol allows several `LOGIN`s per
  connection), instead of the current single monitor.
* UI: an overview with one card per UPS (status, charge, runtime) and the current gauge view as a detail page;
  the tray icon shows the worst state of all UPSes.
* Shutdown: decide per UPS whether it powers this computer (only those can trigger the local stop action).

## Backlog (from milestone 1)
* Verify the WinNUT settings import against a real WinNUT 2.x `user.config` with saved credentials.
* Manual tests with real NUT servers and UPS hardware on Windows, Linux (incl. GNOME tray) and macOS, including a
  real suspend, hibernate and shutdown.
* Native-speaker review of the translations.
* Signed builds (Windows code signing, macOS signing/notarization) and verifying the macOS pkg end to end; later:
  UPS commands (`INSTCMD`, `SET VAR`), TLS (`STARTTLS`).

## Change log

### 2026-10-19 – 0.3.0: Actions system (G3)
* Rule engine replacing the fixed shutdown policy. Triggers: status flag active / became active / became inactive,
  value at or below / at or above a threshold (optionally "only on battery"), on battery for N seconds, connection
  lost for N seconds; level triggers take an optional delay. Rules are evaluated in order. Level rules fire once per
  rising edge; stop rules keep reporting while their condition holds, so the existing countdown, dry-run and re-arm
  safety applies unchanged.
* The former fixed conditions (FSD if "respect FSD", OB+LB, charge floor, runtime floor) are the built-in rules. They
  are still configured on the Power ("Shutdown options") tab and evaluated before the user rules, so existing
  settings keep working without a migration.
* Behaviour change: a pending stop is now only cancelled by "power restored" when it started while on battery. A
  forced shutdown received while on line power is no longer cancelled by the next reading.
* Actions: stop the computer (shutdown, suspend or hibernate), notification, log entry, run a command (no shell,
  `COCONUT_*` environment variables carry the UPS values, killed after its timeout) and webhook (HTTP POST with a JSON
  body of the values, 10 s timeout). Messages support placeholders such as `{ups}`, `{status}`, `{charge}` and
  `{runtime}`. Non-stop actions run once per firing, also while a stop is pending.
* Settings > **Rules**: list with add, duplicate, delete and reorder, an editor for trigger and actions, validation and
  a warning on stop rules; the settings window is larger. User rules are stored in `actions.rules` of the settings
  file.
* The countdown window and the history show "Rule: <name>" for stops started by a user rule; a "rule fired" history
  event is recorded for user rules.
* Connection-lost tracking ends when the user disconnects on purpose.
* Strings in all 7 languages; version raised to 0.3.0.

### 2026-10-19 – 0.2.0: Data history (G2)
* Storage: `history.db` (SQLite) in the data directory. A reading is stored at most every **sample interval** seconds
  (default 10); samples and events older than the **retention** (default 30 days) are deleted, and raw samples older
  than 24 hours are downsampled to 1-minute averages. Charts ask for at most 600 points (averaged buckets); CSV
  exports use full resolution.
* Events recorded: connected, connection lost, on battery, back on line, low battery, forced shutdown, shutdown
  procedure started/cancelled, stop action executing/failed.
* **History** window (File > History and the tray menu): charts of battery charge, battery runtime, load, input
  voltage and output power for the last hour, 6 hours, 24 hours, 7 days or 30 days, with a hover read-out; lines break
  at missing values and recording gaps. UPS selector when several UPSes have history. Event list, newest first, with a
  search over the localized event name and the detail text. Auto-refresh every minute. **Export samples** and
  **Export events** write CSV files.
* Settings > **History**: record on/off, retention days, sample interval, and **Clear history** (asks first).
* New `HistoryChart` control (custom-drawn, no charting package); strings in all 7 languages.
* Version raised to 0.2.0; see Versioning above.

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
