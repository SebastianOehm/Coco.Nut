# TranslationImport

`translation_import.py` (re)generates the six `src/CocoNut.Localization/Strings.<culture>.resx`
files from the original [WinNUT-Client](https://github.com/SebastianOehm/WinNUT-Client)
translations, so none of that community translation work is lost when porting to Coco.Nut.

It is a one-shot / re-runnable *import* step, not something Coco.Nut ships or runs at build
time. `src/CocoNut.Localization/Strings.resx` (the neutral, English resx that defines every key)
is hand-authored and is **not** touched by this script.

## Running it

```sh
python3 tools/TranslationImport/translation_import.py /path/to/WinNUT-Client/checkout
```

The checkout needs the `Translation/` and `WinNUT_V2/` folders from
[SebastianOehm/WinNUT-Client](https://github.com/SebastianOehm/WinNUT-Client) (branch
`dev-2.3`). The script prints a coverage report and writes:

```
src/CocoNut.Localization/Strings.de-DE.resx
src/CocoNut.Localization/Strings.fr-FR.resx
src/CocoNut.Localization/Strings.ru-RU.resx
src/CocoNut.Localization/Strings.uk-UA.resx
src/CocoNut.Localization/Strings.zh-CN.resx
src/CocoNut.Localization/Strings.zh-TW.resx
```

Re-running it is safe and deterministic: it only ever writes translations that come from
`key_map.csv` + the WinNUT checkout, or from `new_strings.csv` (see below), and it always
writes keys in sorted order.

## How a translation is found

For each key in `Strings.resx`, per culture:

1. **`key_map.csv`** (`Key,EnglishSourceText,WinNutResxName`) maps the key to the original
   WinNUT English text and/or an exact WinNUT resx entry (`RelativeResxFile#EntryName`, e.g.
   `Pref_Gui.resx#Btn_Cancel.Text`). If `WinNutResxName` is set, the script looks up that exact
   entry in the culture's copy of that file (`Pref_Gui.<culture>.resx`) - an exact match, so it
   is immune to spelling differences between the English resx and the CSV translation tables.
2. Otherwise (or if step 1 found nothing), the script looks up `EnglishSourceText` in
   `Translation/<culture>/<culture>.csv` (WinNUT's master translation table).
3. **`new_strings.csv`** (`Key,Culture,Translation`), if it has a row for this exact
   (key, culture) pair: its value *replaces* whatever step 1/2 produced (or fills the gap, if
   they produced nothing). Two situations put a row here: the key has no WinNUT equivalent at
   all (a new Coco.Nut string, e.g. `Common_Yes`, the `UpsStatus_*` flags WinNUT never displayed
   individually, or `Language_System`); or the key does have a WinNUT source, but the imported
   translation turned out to be wrong for Coco.Nut in that one culture - most often because the
   neutral English in `keys_data.py` was reworded to drop a Windows-specific mention (Coco.Nut
   also runs on Linux/macOS) or to fix a mistranslation, and the old WinNUT wording no longer
   matches (see `Log_ApplicationShutdownAt`, `Prefs_Misc_StartWithSystem*`,
   `Main_Status_EnteringSleep`/`ResumedFromSleep`, `Log_Level_Warning`/`Notify_Title_Warning` for
   examples, each with a comment above its entry in `new_strings.csv`). Only entries the author
   was confident about are listed; anything else is intentionally left out and falls back to
   English.
4. Otherwise the key is left out of that culture's file entirely, and falls back to English at
   runtime through the normal satellite-assembly / `ResourceManager` fallback.

Every translation resolved through step 1 or 2 also gets the old product name fixed up
(`WinNUT`, `WinNut`, `WinNUT-Client`, ... -> `Coco.Nut`) - a plain brand-name substitution, not a
re-translation; see `apply_brand_fix()` in the script. (The `Prefs_Import_*` strings are an
intentional exception: they are *about* importing settings from the old WinNUT app, so they are
meant to keep saying "WinNUT".)

## Adding or updating a language, as a translator

You do **not** need to touch this script or re-run it for day-to-day translation work. Just
edit the resx files directly - they are plain text:

- To fix or add a translation for an existing key: open `Strings.<culture>.resx`, find (or add)
  the `<data name="TheKey">` element and edit its `<value>`. A key that is missing from a
  culture file simply falls back to the English text from `Strings.resx`.
- To add a new language entirely: copy `Strings.resx`, rename it `Strings.<new-culture>.resx`
  (e.g. `Strings.es-ES.resx`), translate the `<value>` of every `<data>` element, and leave the
  `<comment>` elements out (they only exist in the neutral file, to explain each key to
  translators). Add the culture code to `SatelliteResourceLanguages` in `Directory.Build.props`
  and to the `Cultures` list in `tests/CocoNut.Core.Tests/Localization/StringsResxTests.cs` so
  it is covered by the resx tests, and to `CULTURES` in `translation_import.py` if you also want
  future WinNUT-import re-runs to touch it.
- **Never edit `<comment>` elements or add them to a culture file** - they are developer-facing
  notes (where a string is used, what its placeholders mean) that only live in the neutral
  `Strings.resx`; a comment on a translated entry would be ignored by the resx tooling anyway.
- Keep `{0}`, `{1}`, ... placeholders: a translation must use exactly the same set of
  placeholders as the neutral string (this is enforced by
  `tests/CocoNut.Core.Tests/Localization/StringsResxTests.cs`), though they may appear in a
  different order if the target language's grammar needs that.
- After editing, `dotnet build` regenerates the satellite assembly automatically - no other step
  is required.

If WinNUT itself later gains more translated strings for one of these six languages (e.g. a
`Translation/<culture>/<culture>.csv` update upstream), just re-run `translation_import.py`
against a fresh checkout; it will pick up the new rows for any key already listed in
`key_map.csv` and leave your manual resx edits for keys outside that set untouched (running it
again fully regenerates the six `Strings.<culture>.resx` files from `key_map.csv` +
`new_strings.csv`, so a hand-edit made *outside* of those two input files - e.g. a translator's
own touch-up of a WinNUT-sourced string - will be overwritten; move such a fix into
`new_strings.csv` first if you want it to survive a re-run).

## `Strings.resx` is shared - other work packages add keys to it too

`Strings.resx` (the neutral, English file) is not only edited here: as the rest of Coco.Nut is
built, other work packages add the keys they need directly to it (e.g. WP-G/H added
`Main_Status_Connecting`, `Vars_NotConnected`, `Update_ReleaseName`, and others while building the
app shell and secondary windows). That is expected and fine. What follows from it:

- `translation_import.py` never writes `Strings.resx`; it only *reads* it, to know the full set of
  keys a culture file may legitimately contain and to compute the coverage report. Nothing in this
  directory regenerates that file.
- A key added this way has no WinNUT source, so it starts out English-only, exactly like a
  brand-new key added here - translate it (where confident) via a `new_strings.csv` entry, the
  same as any other new key.
- If you maintain a personal script or notes mirroring `Strings.resx`'s key list (as this
  project's author did, in `keys_data.py`, to build `key_map.csv`), remember it will drift behind
  the committed file whenever another work package adds a key - diff it against the real file
  before trusting it, rather than regenerating `Strings.resx` from it.

## Regression guard

`tests/CocoNut.Core.Tests/Localization/StringsResxTests.cs` has a
`CultureResx_MeetsMinimumCoverageFloor` test per culture that fails the build if that culture's
`Strings.<culture>.resx` translates fewer than **90%** of the neutral keys. This exists because a
WP-B follow-up once rewrote `new_strings_data.py` (the script that generates `new_strings.csv`)
wholesale instead of editing it, and silently dropped 15 keys' worth of translations (90 strings)
from every culture - nothing caught it until a manual per-commit diff of the culture resx files
during review. The 90% floor is set comfortably below every culture's actual coverage at the time
it was added (91.4% for zh-TW, the lowest, up to 95.7% for de-DE) so it only trips on a real,
sizeable loss, not on the normal trickle of newly-added keys nobody has translated yet.

If you deliberately lower a culture's coverage below 90% (e.g. dropping an unmaintained language,
or a large batch of new keys legitimately landing untranslated), lower the constant in that test
and explain why in a comment there, and update the numbers in this section to match. If coverage
instead drops unintentionally, do not lower the floor - restore the missing translations via
`new_strings.csv` (see above), then re-run `translation_import.py`.

## Files

| File | Purpose |
|---|---|
| `translation_import.py` | The importer described above. stdlib-only, Python 3. |
| `key_map.csv` | `Key,EnglishSourceText,WinNutResxName` for every key that has a WinNUT source. |
| `new_strings.csv` | `Key,Culture,Translation`: hand-provided translations for brand-new keys, and corrections that override an imported WinNUT translation for one (key, culture) pair. |
| `README.md` | This file. |
