# Coco.Nut – Migration plan from WinNUT-Client (VB.NET) to C#

Status: **milestone 1 implemented** on branch `claude/intelligent-johnson-32c3oo`: all work packages (0, A-I) are
merged, CI builds and tests on Windows, Linux and macOS. Next: manual testing against real NUT servers and UPS
hardware on all three platforms (see "Open items" at the end).
Source of the port: [SebastianOehm/WinNUT-Client @ dev-2.3](https://github.com/SebastianOehm/WinNUT-Client/tree/dev-2.3)
(pre-release v2.3.9492). Background: [nutdotnet/WinNUT-Client#40](https://github.com/nutdotnet/WinNUT-Client/issues/40).

---

## 1. What WinNUT is today (dev-2.3)

| Area | WinNUT 2.3 | Size |
|---|---|---|
| Language / runtime | VB.NET, .NET Framework 4.8, WinForms, Windows only | ~8.9k lines of VB |
| Projects | `WinNUT-Client` (UI), `WinNUT-Client_Common` (NUT socket, UPS device, logger, updater), `Setup` (vdproj MSI) | |
| NUT protocol | `Nut_Socket.vb`: synchronous `TcpClient`, `VER`/`NETVER`/`USERNAME`/`PASSWORD`/`LOGIN`/`GET VAR`/`GET DESC`/`LIST VAR`; `streamInUse` flag instead of a real lock (known race) | 369 |
| UPS logic | `UPS_Device.vb`: WinForms timer polling, reconnect timer, power-method detection (5 strategies, PR #112/#150), battery-charge estimate from voltage, runtime estimate, `ups.status` → flags, status-diff events, DATA-STALE retries | 593 |
| Main UI | `WinNUT.vb`: 6 AGauge dials (input V, output V, frequency, load, battery V, power), battery bar, status labels, log list, tray icon with composed battery-level icons, toast notifications, suspend/resume handling, stop conditions (charge floor / runtime floor / FSD) | 1057 |
| Other forms | Preferences (6 tabs), UPS variables list (copy / save / refresh), Shutdown countdown (grace extension), About, Update available, Upgrade old (registry/INI) prefs | |
| Settings | `My.Settings` (user.config XML), username/password DPAPI-protected (`SerializedProtectedString`) | 37 settings |
| Logging | own `Logger` class, file in AppData, in-UI log list with line cap | |
| Updates | GitHub releases API, stable / pre-release, downloads MSI | |
| Localization | per-form `.resx` (WinForms designer) + `Resources.resx` for runtime strings, `Translation/*.csv` master translation tables, `.xlf` files; languages **de-DE, fr-FR, ru-RU, uk-UA, zh-CN, zh-TW** (+ en) | |

### Pain points we fix while porting
* WinForms + VB + .NET Framework 4.8: legacy stack, Windows only (the reason for issue #40).
* Designer-generated per-form resx: translations scattered across ~50 files, hard to review, keys are control names.
* Blocking socket I/O on UI timers, race in `Query_Data` / `Query_List_Datas`, exceptions as control flow.
* Business logic (stop conditions, power calculation) lives inside forms → untestable. No unit tests.
* Unknown `ups.status` tokens made `Enum.Parse` fail and keep the old status.

---

## 2. Target architecture

**Decision:** C# 13 / **.NET 10 (LTS)**, **Avalonia UI 11.3** (MVVM with CommunityToolkit.Mvvm), **resx** localization.

Why Avalonia instead of MAUI (mentioned in #40): MAUI has no Linux desktop target and a heavy toolchain; Avalonia
runs on Windows, Linux and macOS, has a tray icon API, builds/tests on any CI runner and keeps XAML skills.
Avalonia 12 was released recently; 11.3 is chosen for stability and can be upgraded later in isolation.

Why not reuse the old `nutdotnet` library: last updated 2022, MVP state. A small, fully tested async client is
written in `CocoNut.Core.Nut` behind `INutClient`, so it can later be extracted into / replaced by that library.

```
Coco.Nut/
├─ CocoNut.sln, global.json, Directory.Build.props, Directory.Packages.props (central package versions)
├─ src/
│  ├─ CocoNut.Core/          net10.0, no UI dependency
│  │   ├─ Nut/               INutClient, NutClient (async TCP, SemaphoreSlim-serialized), NutException, parser
│  │   ├─ Ups/               UpsStatus flags + parser, UpsReading, UpsInfo, PowerMethod, calculators
│  │   ├─ Monitoring/        UpsMonitor: connect/login/poll/reconnect loop, events (StateChanged, ReadingUpdated,
│  │   │                     StatusChanged); DATA-STALE handling; product info; variable listing
│  │   ├─ Shutdown/          ShutdownPolicy (charge floor, runtime floor, FSD, back-online cancels),
│  │   │                     ShutdownCountdown (delay, one-time extension)  → pure logic, unit tested
│  │   ├─ Settings/          AppSettings (defaults = WinNUT), JsonSettingsStore, secret protection
│  │   │                     (DPAPI on Windows, AES key file with 0600 perms elsewhere), WinNUT settings import
│  │   ├─ Logging/           file logger provider + in-memory ring buffer for the UI log view
│  │   ├─ Updates/           GitHub release checker (stable / pre-release channel, notify only)
│  │   └─ Abstractions/      IPowerActions, IAutoStartService, INotificationService
│  ├─ CocoNut.Localization/  Strings.resx (neutral = English) + Strings.{de-DE,fr-FR,ru-RU,uk-UA,zh-CN,zh-TW}.resx,
│  │                         strongly typed `Strings` class generated by MSBuild (no VS designer needed)
│  ├─ CocoNut.Platform/      Windows (shutdown.exe, SetSuspendState, HKCU Run key), Linux (systemctl,
│  │                         XDG autostart), macOS (pmset / osascript, LaunchAgent) implementations
│  └─ CocoNut.App/           Avalonia desktop app: Views + ViewModels, Gauge control, tray icon,
│                            in-app notification popup, DI composition root
├─ tests/CocoNut.Core.Tests/ xUnit: protocol against a fake NUT server, calculators, policy, settings,
│                            resx completeness (all keys translated, same {0} placeholders)
├─ tools/TranslationImport/  one-shot script: WinNUT Translation/*.csv + Resources*.resx → Strings.*.resx
└─ .github/workflows/        build + test on ubuntu-latest and windows-latest
```

### Localization with resx
* One `Strings.resx` with **semantic keys** (`Main_Status_OnBattery`, `Prefs_Tab_Connection`, …) instead of control
  names. Translations in `Strings.<culture>.resx` → satellite assemblies.
* XAML uses `{x:Static loc:Strings.Key}`; code uses `Strings.Key` / `string.Format(Strings.Culture, …)`.
* Language chosen in Preferences (`GeneralSettings.Language`, null = OS language); applied at startup by setting
  `CultureInfo.DefaultThreadCurrentUICulture` and `Strings.Culture` (restart required after change).
* Existing translations are imported from WinNUT's `Translation/<culture>/<culture>.csv` (English text → translation)
  and the localized `Resources.<culture>.resx` so no translation work is lost. uk-UA has no CSV; its strings come from
  WinNUT's `*.uk-UA.resx` files.
* A unit test guarantees every culture file contains only known keys and that format placeholders match the neutral
  string (missing keys fall back to English).
* File logs stay English (as in WinNUT) so bug reports are readable; UI-visible messages are localized.

### Feature mapping (WinNUT → Coco.Nut)

| WinNUT | Coco.Nut |
|---|---|
| `Nut_Socket.vb` | `Core/Nut/NutClient.cs` |
| `UPS_Device.vb` | `Core/Monitoring/UpsMonitor.cs`, `Core/Ups/*Calculator.cs`, `Core/Ups/UpsStatusParser.cs` |
| `Common_Classes.vb`, `Common_Enums.vb` | `Core/Nut/*`, `Core/Ups/*` |
| `Logger.vb` | `Microsoft.Extensions.Logging` + `Core/Logging/*` |
| `SerializedProtectedString.vb`, `My.Settings` | `Core/Settings/*` (JSON in the per-user app-data folder) |
| `OldParams/*`, `UpgradePrefsDialog` | `Core/Settings/WinNutSettingsImporter` (import WinNUT 2.x `user.config`; 1.x INI/registry import dropped) |
| `Updater/*`, `UpdateAvailableForm` | `Core/Updates/UpdateChecker` + `App/Views/UpdateAvailableWindow` (opens release page; no self-install) |
| `WinNUT.vb` (+ Designer) | `App/Views/MainWindow` + `MainWindowViewModel` + `TrayIconController` |
| AGauge / `UPSVarGauge.vb` | `App/Controls/Gauge` (custom-drawn Avalonia control) |
| `CProgressBar.vb` | Avalonia `ProgressBar` with text template |
| `Pref_Gui.vb` | `App/Views/SettingsWindow` + `SettingsViewModel` (tabs: Connection, Calibration, Logging, Power, Misc, Update) |
| `List_Var_Gui.vb` | `App/Views/UpsVariablesWindow` (search, copy, save as text) |
| `Shutdown_Gui.vb` | `App/Views/ShutdownWindow` driven by `Core/Shutdown/ShutdownCountdown` |
| `About_Gui.vb` | `App/Views/AboutWindow` |
| `ToastPopup.vb` (Win10 toasts) | `INotificationService` → in-app popup window (all OS) |
| Windows power actions / StartWithWindows | `CocoNut.Platform` |
| `Setup.vdproj` MSI | later: `dotnet publish` self-contained + installer (out of scope for first milestone) |

---

## 3. Execution plan (sub-agents)

Roles: the **architect/reviewer** (lead session) owns the skeleton, the contracts in `Core/Abstractions`, `Core/Nut/INutClient.cs`,
`Core/Ups/*`, `Core/Settings/AppSettings.cs`, reviews every work package and merges it. **Implementer sub-agents run
on the Sonnet model**, each in its own git worktree, one work package each, and must leave `dotnet build` (warnings
as errors) and `dotnet test` green.

| Wave | WP | Scope | Depends on | Status |
|---|---|---|---|---|
| 0 | Skeleton | solution, props, contracts, empty app, this plan | – | Done |
| 1 | **A** NUT client | `NutClient` + fake NUT server test harness + protocol tests | 0 | Done |
| 1 | **B** Localization | import tool, `Strings*.resx` for all 7 languages, resx completeness tests | 0 | Done |
| 1 | **C** Platform | power actions, autostart for Windows/Linux/macOS + tests of command construction | 0 | Done |
| 1 | **D** Settings/Logging/Updates | JSON store + secret protection, WinNUT import, file logger + ring buffer, update checker | 0 | Done |
| 2 | **E** Monitor + Shutdown | `UpsMonitor`, calculators, status parser, `ShutdownPolicy`, `ShutdownCountdown` + tests (fake `INutClient`) | A | Done |
| 2 | **F** Gauge + tray icons | `Gauge` control, battery/tray icon composition | 0 | Done |
| 3 | **G** App shell | DI, MainWindow + VM, tray, notifications popup, Shutdown window, language startup | B–F | Done |
| 3 | **H** Secondary windows | Settings, UPS variables, About, Update available windows + VMs | B, D, G-shell contracts | Done |
| 4 | **I** CI + docs | GitHub Actions (ubuntu, windows, macos), README, CONTRIBUTING (translations) | all | Done |

Review checklist applied to every WP: matches the contracts; no blocking I/O on the UI thread; cancellation honoured;
no secrets in logs; invariant culture for NUT parsing; strings from `Strings` (no hard-coded UI text); tests cover the
WinNUT behaviours listed in the WP; build has zero warnings.

### Out of scope for milestone 1
Installer/MSI, auto-download of updates, multiple UPS at once, instant commands (`INSTCMD`), `SET VAR`, TLS (`STARTTLS`),
mobile targets. These are follow-ups once the port reaches feature parity.

### How the work was done
Every work package was implemented by a Sonnet sub-agent in its own git worktree and reviewed by the architect before
merging. Reviews went beyond reading the diff: stress runs of the test suite (flaky fake-clock tests were found and
fixed), rendering screenshots, and an end-to-end run of the real app under Xvfb against a scripted fake NUT server with
`COCONUT_DRY_RUN=1` (outage → countdown → dry-run execution → reconnect → re-arm; power restored → cancel). Bugs found
that way and fixed include: the shutdown coordinator never recovering after a suspend/failed action, a stale
shutdown window, gauge read-outs showing clamped values, a second instance crashing, macOS socket path limits, and
lost translations.

### Open items
Future work and the change log are tracked in [`ROADMAP.md`](ROADMAP.md).

* Manual tests on real hardware/NUT servers (Windows, Linux desktop environments incl. GNOME tray behaviour, macOS),
  including a real suspend/hibernate/shutdown.
* WinNUT `user.config` import: the credential format is inferred, not verified against a real file.
* Host name / IP format validation in the settings (WinNUT had it; only "not empty" is checked now).
* Windows hibernate capability check (`IsSupported(Hibernate)` currently always true).
* Native-speaker review of the translations (several were machine-translated or carried over from WinNUT verbatim).
* Packaging (installer, signed builds) and the other out-of-scope items above.
