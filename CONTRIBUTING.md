# Contributing to Coco.Nut

Thanks for your interest in Coco.Nut. This document covers the development setup, the conventions the codebase
follows, how to work with translations, and what to check before opening a pull request.

## Development setup

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), the exact version is pinned in
[`global.json`](global.json) (SDK `10.0.100`, roll-forward to the latest feature band). No other tooling is
required - Avalonia's XAML previewer works from the .NET CLI, and a full IDE (Visual Studio, Rider, VS Code with the
C# Dev Kit) is optional but convenient for the XAML designer.

```sh
git clone https://github.com/SebastianOehm/Coco.Nut.git
cd Coco.Nut
dotnet restore
dotnet build
dotnet test
```

`export AVALONIA_TELEMETRY_OPTOUT=1` before running Avalonia CLI commands if you don't want telemetry prompts.

Run the app with `dotnet run --project src/CocoNut.App`. See the README for `COCONUT_DRY_RUN` and
`COCONUT_DATA_DIR`, which are useful while developing so you don't trigger a real shutdown or touch your real
settings file.

## Coding conventions

These are enforced by the build (as errors, not suggestions) or by `.editorconfig`/`Directory.Build.props`:

- **Nullable reference types are enabled** (`Nullable=enable`) project-wide. Don't add `#nullable disable`; fix the
  warning instead.
- **Warnings are treated as errors** (`TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`). A PR that
  doesn't build clean won't pass CI.
- **File-scoped namespaces** (`namespace Foo.Bar;`, not `namespace Foo.Bar { ... }`) - enforced by `.editorconfig`.
- **Private fields are `_camelCase`** (leading underscore, camel case) - see `.editorconfig`'s naming rules.
- **MVVM with CommunityToolkit.Mvvm**: view models derive from `ObservableObject` (or use `[ObservableProperty]` /
  `[RelayCommand]` source generators), views bind to them; avoid putting logic that isn't view-related into a
  view's code-behind. Business logic (protocol parsing, shutdown policy, calculations) belongs in `CocoNut.Core`,
  which has no UI dependency and is unit tested without Avalonia.
- **No hard-coded UI strings.** Anything a user can see (window/control text, notification text, log messages meant
  for the UI log view) must come from `CocoNut.Localization.Strings`, not a literal in code or XAML. See
  "Adding or changing strings" below. (File logs written through `Microsoft.Extensions.Logging` are the one
  exception - they intentionally stay English so bug reports are readable regardless of the user's language.)
- Prefer `async`/`await` and cancellation tokens over blocking calls; nothing should block the UI thread. NUT
  protocol I/O in particular must go through the async `INutClient`.
- Use the invariant culture for parsing/formatting values that come from or go to the NUT protocol; use the current
  UI culture only for user-facing display.

## Tests

```sh
dotnet test
```

`tests/CocoNut.Core.Tests` covers the NUT protocol client (against a fake in-process NUT server), the UPS
calculators and status parser, the shutdown policy/countdown, settings storage and secret protection, the platform
abstractions, and the resx completeness/consistency checks described below. `tests/CocoNut.App.Tests` covers
Avalonia-specific pieces (e.g. the gauge control, tray icon selection) using `Avalonia.Headless.XUnit`.

New behavior should come with tests at the same level it was ported from WinNUT at (pure logic in `CocoNut.Core` is
the easiest to test thoroughly; keep platform- and UI-specific code as thin as possible so most of the logic stays
testable without a display).

## Adding or changing strings and translations

All user-visible text lives in `src/CocoNut.Localization/Strings.resx` (the neutral, English resx) plus one
`Strings.<culture>.resx` per supported language. See
[`tools/TranslationImport/README.md`](tools/TranslationImport/README.md) for the full details of how translations
are imported and maintained; the short version:

- **Key naming**: `<Area>_<Element>[_<Detail>]`, e.g. `Main_Status_OnBattery`, `Prefs_Tab_Connection`,
  `Log_ApplicationShutdownAt`. Keys are semantic (what the string is/means), not tied to a control name.
- **Every key in the neutral `Strings.resx` needs a `<comment>`** describing where it's used and what any `{0}`,
  `{1}`, ... placeholders mean - this is enforced by `StringsResxTests.NeutralResx_EveryKeyHasAComment`. Comments
  are developer-facing notes for translators and must **not** be added to culture-specific resx files.
- Adding or editing an English string: edit `Strings.resx` directly (add the `<data>` element with a `<comment>`,
  or change an existing `<value>`). `dotnet build` regenerates the strongly-typed `Strings` class automatically.
- Adding or fixing a translation for an existing language: edit `Strings.<culture>.resx` directly, no tooling
  needed. A key missing from a culture file simply falls back to English at runtime.
- **Placeholders must match**: a translated value must use exactly the same set of `{0}`, `{1}`, ... placeholders as
  the neutral string (order can differ if the target language's grammar needs it). This is checked by
  `tests/CocoNut.Core.Tests/Localization/StringsResxTests.cs` (`CultureResx_PlaceholdersMatchTheNeutralString`) and
  will fail CI if you get it wrong.
- Use `{x:Static loc:Strings.Key}` in XAML and `Strings.Key` / `string.Format(Strings.Culture, ...)` in code - never
  a literal string for anything user-visible.

### Adding a new language

1. Copy `src/CocoNut.Localization/Strings.resx` to `Strings.<new-culture>.resx` (e.g. `Strings.es-ES.resx`) and
   translate every `<value>`. Leave out the `<comment>` elements - they only belong in the neutral file.
2. Add the culture code to `SatelliteResourceLanguages` in [`Directory.Build.props`](Directory.Build.props).
3. Add a `Language_xx_YY` key (e.g. `Language_es_ES`) to `Strings.resx` (with its translation in every culture file,
   including the new one) so the language shows up correctly in the language picker.
4. Add the culture to the `Cultures` list in
   `tests/CocoNut.Core.Tests/Localization/StringsResxTests.cs` so it is covered by the resx consistency tests.
5. If you'd like a future re-run of `tools/TranslationImport/translation_import.py` against a WinNUT checkout to
   also populate this language, add it to `CULTURES` in that script too - optional, since WinNUT itself may not
   have shipped that language.
6. `dotnet build` picks up the new satellite resources automatically; `dotnet test` verifies the new file.

## Pull request checklist

Before opening a PR, please make sure:

- [ ] `dotnet build -c Release` succeeds with zero warnings.
- [ ] `dotnet test -c Release` passes.
- [ ] No hard-coded user-visible strings were introduced; new strings were added to `Strings.resx` with a comment.
- [ ] If you touched a culture resx file, placeholders still match the neutral string.
- [ ] New logic that isn't tied to Avalonia lives in (and is tested in) `CocoNut.Core`, not in a view or
      code-behind.
- [ ] No secrets (NUT passwords, tokens) end up in logs or committed files.
- [ ] The PR description explains *why*, not just *what*, for anything beyond a trivial fix.

CI (`.github/workflows/build.yml`) runs the build and test matrix (Linux, Windows, macOS) on every push and pull
request; a `publish` job also builds self-contained single-file binaries for `win-x64`, `linux-x64` and `osx-arm64`
on pushes to `main` and version tags (`v*`).
