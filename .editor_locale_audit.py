import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

ROOT = pathlib.Path(__file__).resolve().parent
UI = ROOT / "Resources/ui/lang"
DESKTOP = ROOT / "Desktop"
LOCALES = sorted(p for p in UI.rglob("*.json") if p.stem != "en-GB" )
PLACEHOLDER = re.compile(r"\{\d+(?:[^}]*)?\}|%[^%\s]+%")
MOJIBAKE_MARKERS = ("Ã", "Â", "â€", "ðŸ", "ä»", "æ—", "�", "ï»¿")
KEY_CALL = re.compile(r'UiStrings\.(?:Get|GetOrNull|Format)\(\s*"([^"]+)"')
LOCALIZE_CALL = re.compile(r'UiStrings\.LocalizeText\(\s*"([^"]+)"')


class DuplicateKeyError(ValueError):
    pass


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_table(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_pairs)


def normalize(value):
    value = str(value).replace("\\n", " ").replace("\\r", " ").replace("\\t", " ")
    return " ".join(value.replace("&", "").replace("…", "...").split()).casefold()


def placeholders(value):
    return Counter(token.upper() if token.startswith("%") else token for token in PLACEHOLDER.findall(str(value)))


def main():
    failures = defaultdict(list)
    advisories = defaultdict(list)
    try:
        english = read_table(UI / "en-GB.json")
    except Exception as exc:
        print(f"FATAL English source table: {type(exc).__name__}: {exc}")
        return 2

    english_keys = {key for key in english if not key.startswith("_meta.")}
    english_text = {normalize(value) for key, value in english.items()
                    if key in english_keys and isinstance(value, str)}
    source_key_refs = defaultdict(list)
    source_dynamic_prefixes = set()
    source_literal_refs = set()

    source_files = [p for p in DESKTOP.rglob("*.cs")
                    if not any(part in {"bin", "obj"} for part in p.parts)]
    for path in source_files:
        try:
            source = path.read_text(encoding="utf-8", errors="replace")
        except OSError as exc:
            failures["unreadable-source"].append((str(path.relative_to(ROOT)), str(exc)))
            continue
        for key in KEY_CALL.findall(source):
            source_key_refs[key].append(str(path.relative_to(ROOT)))
            if key.endswith((".", "_")):
                source_dynamic_prefixes.add((str(path.relative_to(ROOT)), key))
        for text in LOCALIZE_CALL.findall(source):
            if normalize(text) not in english_text:
                source_literal_refs.add((str(path.relative_to(ROOT)), text.replace("\\n", " ")))

    for path in sorted(DESKTOP.rglob("*.xaml")):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        try:
            root = ET.parse(path).getroot()
        except (ET.ParseError, OSError) as exc:
            failures["invalid-xaml"].append((str(path.relative_to(ROOT)), str(exc)))
            continue
        for element in root.iter():
            for attribute, value in element.attrib.items():
                name = attribute.split("}")[-1]
                if name not in {"Text", "Content", "ToolTip", "Title", "Header"}:
                    continue
                if not value or value.startswith("{") or normalize(value) in english_text:
                    continue
                if normalize(value) in {"nmsfe", "—", "□", "✕", "⌄"}:
                    continue
                advisories["xaml-literal-not-in-source-table"].append(
                    (str(path.relative_to(ROOT)), name, value[:160]))

    for key, refs in sorted(source_key_refs.items()):
        if key not in english and not key.endswith((".", "_")):
            failures["source-key-missing-from-en-GB"].append((key, refs[:3]))
    for locale in LOCALES:
        try:
            table = read_table(locale)
        except Exception as exc:
            failures["invalid-locale-json"].append((locale.name, f"{type(exc).__name__}: {exc}"))
            continue
        missing = english_keys - set(table)
        extra = {key for key in table if not key.startswith("_meta.")} - english_keys
        if missing:
            failures["missing-keys"].append((locale.stem, len(missing), sorted(missing)[:8]))
        if extra:
            advisories["extra-keys"].append((locale.stem, len(extra), sorted(extra)[:8]))
        for key in english_keys & set(table):
            value = table[key]
            if not isinstance(value, str):
                failures["non-string-value"].append((locale.stem, key, type(value).__name__))
                continue
            if not value.strip():
                failures["empty-value"].append((locale.stem, key))
            if placeholders(english[key]) != placeholders(value):
                failures["placeholder-mismatch"].append((locale.stem, key, placeholders(english[key]), placeholders(value)))
            if any(marker in value for marker in MOJIBAKE_MARKERS):
                failures["possible-mojibake"].append((locale.stem, key, value[:100]))
            if value == english[key] and key not in {"_meta.description", "_meta.version"}:
                advisories["english-carryover"].append((locale.stem, key, value[:100]))

    print(f"UI_LOCALE_AUDIT locales={len(LOCALES)} source_keys={len(english_keys)} recursive_cs={len(source_files)}")
    for category, entries in sorted(failures.items()):
        print(f"FAIL {category}: {len(entries)}")
        for entry in entries[:12]:
            print("  ", entry)
    for category, entries in sorted(advisories.items()):
        print(f"NOTE {category}: {len(entries)}")
        for entry in entries[:8]:
            print("  ", entry)
    print(f"DYNAMIC_KEY_PREFIXES (review generated suffix coverage): {len(source_dynamic_prefixes)}")
    for item in sorted(source_dynamic_prefixes):
        print("  ", item)
    print(f"UNCOVERED_LITERAL_CALLS (needs key or explicit allowlist): {len(source_literal_refs)}")
    for item in sorted(source_literal_refs)[:20]:
        print("  ", item)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
