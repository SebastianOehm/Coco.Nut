#!/usr/bin/env python3
"""
translation_import.py -- (re)generates CocoNut.Localization's Strings.<culture>.resx files
from the original WinNUT-Client translations, so no existing translation work is lost when
porting WinNUT to Coco.Nut.

Usage:
    python3 translation_import.py <path-to-winnut-checkout> [--out-dir DIR]

    <path-to-winnut-checkout>  Root of a checkout of SebastianOehm/WinNUT-Client (dev-2.3),
                                the folder that contains "Translation/" and "WinNUT_V2/".

Inputs (read from this script's own directory):
    key_map.csv       Key,EnglishSourceText,WinNutResxName
                       One row per semantic Strings.resx key that has a WinNUT source. Maps
                       the key to (a) the original English WinNUT text, used to look up a row
                       in Translation/<culture>/<culture>.csv, and/or (b) "RelativeResxFile
                       #EntryName" pointing at the exact WinNUT resx <data> entry that carries
                       this string (relative to WinNUT_V2/WinNUT-Client/). WinNutResxName may
                       be blank when only the CSV lookup applies.

    new_strings.csv   Key,Culture,Translation
                       Hand-provided translations, applied after (and overriding) whatever step
                       1/2 below produced for that exact (key, culture) pair. Two situations put
                       a row here: (a) the key does not exist in WinNUT at all (new Coco.Nut
                       strings such as language names or new UpsStatus flags), or (b) the key
                       does have a WinNUT source, but the imported translation is wrong for
                       Coco.Nut for that one culture (typically because the neutral English was
                       rewritten to drop a Windows-specific mention or fix a mistranslation, and
                       the old WinNUT wording no longer matches - see keys_data.py's comments).
                       Only entries the author was confident about are listed here; anything
                       else is intentionally left out and falls back to English.

Reads from the WinNUT checkout:
    Translation/<culture>/<culture>.csv         Status,Translator,Message,Translation
    WinNUT_V2/WinNUT-Client/<RelativeResxFile with ".<culture>" inserted before ".resx">
                                                 the original per-form / per-resource culture
                                                 resx files (WinNUT.<culture>.resx, My Project/
                                                 Resources.<culture>.resx, ...)

Writes (relative to this script, i.e. tools/TranslationImport/../../):
    src/CocoNut.Localization/Strings.<culture>.resx   for every culture in CULTURES below.

Resolution order per key, independently for each culture (see resolve_translation() and main()):
    1. WinNutResxName, if given: look up EntryName directly in that culture's copy of
       RelativeResxFile. Exact match on the WinNUT control/resource name, so it is immune to
       wording differences (typos, later rewordings) between the English resx and the CSV.
    2. Otherwise (or if step 1 found nothing): normalize EnglishSourceText (trim, collapse
       whitespace/newlines to single spaces) and look it up the same way in
       Translation/<culture>/<culture>.csv.
    3. new_strings.csv, if it has a row for this exact (key, culture): its value replaces
       whatever step 1/2 produced (or fills the gap, if they produced nothing).
    4. Otherwise the key is left out of that culture's resx: it falls back to English at
       runtime through the normal satellite-assembly fallback.

This script never invents a translation: every value it writes came verbatim from the WinNUT
checkout (steps 1/2) or from new_strings.csv (hand-provided, reviewed by the author). It is
deterministic (keys are written in sorted order) and safe to re-run at any time, e.g. after
key_map.csv gains new rows or the WinNUT checkout is updated.
"""
from __future__ import annotations

import argparse
import csv
import re
from pathlib import Path
from xml.etree import ElementTree as ET
from xml.sax.saxutils import escape

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent
LOCALIZATION_DIR = REPO_ROOT / "src" / "CocoNut.Localization"
NEUTRAL_RESX = LOCALIZATION_DIR / "Strings.resx"
KEY_MAP_CSV = SCRIPT_DIR / "key_map.csv"
NEW_STRINGS_CSV = SCRIPT_DIR / "new_strings.csv"

# WinNUT's own resx files live under this folder in the checkout.
WINNUT_CLIENT_DIR = "WinNUT_V2/WinNUT-Client"

CULTURES = ["de-DE", "fr-FR", "ru-RU", "uk-UA", "zh-CN", "zh-TW"]

RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""


def normalize(text: str) -> str:
    """Collapse all whitespace (including embedded newlines) to single spaces and trim.
    WinNUT's Translation/*.csv flattens multi-line resx values onto one line, and a few
    translations carry a stray leading space, so English text is compared this way."""
    return re.sub(r"\s+", " ", text).strip()


# WinNUT's own translators sometimes wrote the product name into a translation (e.g. the
# German word for "About" is translated as "Über WinNUT"). Coco.Nut is a rebrand, so every
# translation pulled in from WinNUT has its spelling of the old name replaced with the new one.
# This is a plain brand-name substitution (safe in every target language, like replacing a
# company name), not a re-translation: other WinNUT/Windows-specific wording that a string may
# still carry is intentionally left untouched, see the README and the WP-B report.
_BRAND_PATTERN = re.compile(r"WinNUT-Client|WinNUT|WinNut|Winnut|winnut")


def apply_brand_fix(text: str) -> str:
    return _BRAND_PATTERN.sub("Coco.Nut", text)


def parse_key_map(path: Path):
    """Returns a list of dicts: key, eng_source, resx_file (relative, or None), resx_entry."""
    rows = []
    with path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            key = row["Key"].strip()
            eng_source = row["EnglishSourceText"]
            resx_ref = (row.get("WinNutResxName") or "").strip()
            resx_file = resx_entry = None
            if resx_ref:
                if "#" not in resx_ref:
                    raise SystemExit(f"key_map.csv: {key}: WinNutResxName {resx_ref!r} missing '#'")
                resx_file, resx_entry = resx_ref.split("#", 1)
            rows.append(dict(key=key, eng_source=eng_source, resx_file=resx_file, resx_entry=resx_entry))
    return rows


def parse_new_strings(path: Path):
    """Returns dict: key -> {culture: translation}."""
    out: dict[str, dict[str, str]] = {}
    if not path.exists():
        return out
    with path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            key = row["Key"].strip()
            culture = row["Culture"].strip()
            translation = row["Translation"]
            out.setdefault(key, {})[culture] = translation
    return out


def load_neutral_keys(path: Path) -> dict[str, str]:
    """Returns dict: key -> neutral English value, read from the hand-authored Strings.resx."""
    tree = ET.parse(path)
    keys = {}
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        value_el = data.find("value")
        keys[name] = value_el.text or "" if value_el is not None else ""
    return keys


def load_csv_translations(winnut_root: Path, culture: str) -> dict[str, str]:
    """Returns dict: normalized English message -> translation, from
    Translation/<culture>/<culture>.csv. Missing file (e.g. uk-UA has none) -> empty dict."""
    csv_path = winnut_root / "Translation" / culture / f"{culture}.csv"
    table: dict[str, str] = {}
    if not csv_path.exists():
        return table
    with csv_path.open(encoding="utf-8-sig", newline="") as f:
        rows = list(csv.reader(f))
    for row in rows[1:]:
        if len(row) < 4:
            continue
        message, translation = row[2], row[3]
        if not translation.strip():
            continue
        table[normalize(message)] = translation.strip()
    return table


_resx_cache: dict[Path, dict[str, str] | None] = {}


def load_resx_entries(path: Path) -> dict[str, str] | None:
    """Returns dict: entry name -> text value for a single .resx file, or None if the file
    does not exist / cannot be parsed. Cached because several keys share the same file."""
    if path in _resx_cache:
        return _resx_cache[path]
    if not path.exists():
        _resx_cache[path] = None
        return None
    try:
        tree = ET.parse(path)
    except ET.ParseError:
        _resx_cache[path] = None
        return None
    entries = {}
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        value_el = data.find("value")
        entries[name] = value_el.text if value_el is not None and value_el.text else ""
    _resx_cache[path] = entries
    return entries


def culture_resx_path(winnut_root: Path, relative_file: str, culture: str) -> Path:
    """WinNUT.resx -> WinNUT.<culture>.resx ; My Project/Resources.resx -> My Project/
    Resources.<culture>.resx ; Forms/UpdateAvailableForm.resx -> Forms/UpdateAvailableForm.
    <culture>.resx."""
    if not relative_file.endswith(".resx"):
        raise ValueError(f"expected a .resx path, got {relative_file!r}")
    stem = relative_file[: -len(".resx")]
    return winnut_root / WINNUT_CLIENT_DIR / f"{stem}.{culture}.resx"


def resolve_translation(row: dict, culture: str, winnut_root: Path, csv_table: dict[str, str]) -> str | None:
    """Implements the 2-step resolution order documented at the top of this file. The result
    (if any) has apply_brand_fix() applied, since it came verbatim from a WinNUT source."""
    if row["resx_file"]:
        resx_path = culture_resx_path(winnut_root, row["resx_file"], culture)
        entries = load_resx_entries(resx_path)
        if entries is not None:
            value = entries.get(row["resx_entry"])
            if value:
                return apply_brand_fix(value)
    eng_source = normalize(row["eng_source"])
    value = csv_table.get(eng_source)
    return apply_brand_fix(value) if value is not None else None


def write_culture_resx(path: Path, items: dict[str, str]) -> None:
    lines = [RESX_HEADER]
    for key in sorted(items):
        value = items[key]
        lines.append(f'  <data name="{escape(key)}" xml:space="preserve">\n')
        lines.append(f"    <value>{escape(value)}</value>\n")
        lines.append("  </data>\n")
    lines.append("</root>\n")
    path.write_text("".join(lines), encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("winnut_root", type=Path, help="Root of the WinNUT-Client checkout")
    parser.add_argument("--out-dir", type=Path, default=LOCALIZATION_DIR,
                         help=f"Where to write Strings.<culture>.resx (default: {LOCALIZATION_DIR})")
    args = parser.parse_args()

    winnut_root: Path = args.winnut_root.resolve()
    if not (winnut_root / "Translation").is_dir() or not (winnut_root / "WinNUT_V2").is_dir():
        parser.error(f"{winnut_root} does not look like a WinNUT-Client checkout "
                      "(expected Translation/ and WinNUT_V2/ subfolders)")

    neutral_keys = load_neutral_keys(NEUTRAL_RESX)
    key_map_rows = parse_key_map(KEY_MAP_CSV)
    new_strings = parse_new_strings(NEW_STRINGS_CSV)

    unknown = [r["key"] for r in key_map_rows if r["key"] not in neutral_keys]
    if unknown:
        raise SystemExit(f"key_map.csv references keys missing from Strings.resx: {unknown}")
    unknown_new = [k for k in new_strings if k not in neutral_keys]
    if unknown_new:
        raise SystemExit(f"new_strings.csv references keys missing from Strings.resx: {unknown_new}")

    total = len(neutral_keys)
    print(f"Neutral Strings.resx has {total} keys.")
    args.out_dir.mkdir(parents=True, exist_ok=True)

    report_rows = []
    for culture in CULTURES:
        csv_table = load_csv_translations(winnut_root, culture)
        translated: dict[str, str] = {}
        # Which of the three sources below the *final* value for each key came from, so a
        # new_strings.csv override/correction of a key_map-resolved value is counted once, under
        # "manual" - not twice (see the docstring's 4-step resolution order).
        source: dict[str, str] = {}

        for row in key_map_rows:
            value = resolve_translation(row, culture, winnut_root, csv_table)
            if value is None:
                continue
            translated[row["key"]] = value
            if row["resx_file"]:
                resx_path = culture_resx_path(winnut_root, row["resx_file"], culture)
                entries = load_resx_entries(resx_path) or {}
                source[row["key"]] = "resx" if entries.get(row["resx_entry"]) else "csv"
            else:
                source[row["key"]] = "csv"

        for key, per_culture in new_strings.items():
            if culture in per_culture and per_culture[culture].strip():
                translated[key] = per_culture[culture]
                source[key] = "manual"

        via_resx = sum(1 for s in source.values() if s == "resx")
        via_csv = sum(1 for s in source.values() if s == "csv")
        via_manual = sum(1 for s in source.values() if s == "manual")

        out_path = args.out_dir / f"Strings.{culture}.resx"
        write_culture_resx(out_path, translated)
        report_rows.append((culture, len(translated), total, via_resx, via_csv, via_manual))

    print()
    print(f"{'Culture':<8} {'Translated':>10} / {'Total':<5}  {'%':>6}   (resx / csv / manual)")
    for culture, count, tot, via_resx, via_csv, via_manual in report_rows:
        pct = 100.0 * count / tot if tot else 0.0
        print(f"{culture:<8} {count:>10} / {tot:<5}  {pct:5.1f}%   ({via_resx} / {via_csv} / {via_manual})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
