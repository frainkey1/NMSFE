import collections
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent
BASE = ROOT / "Resources/json"
LANG = BASE / "lang"
LOCALES = sorted(path for path in LANG.glob("*.json") if path.stem != "en-GB")
TOKEN = re.compile(r"\{\d+(?:[^}]*)?\}|%[A-Za-z][A-Za-z0-9_]*%")
REFERENCES = ("Name_LocStr", "Subtitle_LocStr", "Description_LocStr", "Category_LocStr")


class DuplicateKeyError(ValueError):
    pass


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_pairs)


def tokens(value):
    return collections.Counter(item.upper() if item.startswith("%") else item for item in TOKEN.findall(str(value)))


def main():
    failures = collections.defaultdict(list)
    advisories = collections.defaultdict(list)
    try:
        english = read_json(LANG / "en-GB.json")
    except Exception as exc:
        print(f"FATAL English game table: {type(exc).__name__}: {exc}")
        return 2

    used = collections.defaultdict(list)
    item_files = [path for path in BASE.glob("*.json") if path.parent != LANG]
    for path in item_files:
        try:
            data = read_json(path)
        except Exception as exc:
            failures["unreadable-item-json"].append((path.name, f"{type(exc).__name__}: {exc}"))
            continue
        stack = [data]
        while stack:
            value = stack.pop()
            if isinstance(value, dict):
                ident = value.get("Id", "")
                for field in REFERENCES:
                    key = value.get(field)
                    if isinstance(key, str) and key.strip():
                        used[key].append((field, str(ident), path.name))
                stack.extend(value.values())
            elif isinstance(value, list):
                stack.extend(value)

    for key in sorted(set(used) - set(english)):
        advisories["referenced-key-missing-from-en-GB"].append((key, len(used[key]), used[key][:2]))

    for path in LOCALES:
        try:
            table = read_json(path)
        except Exception as exc:
            failures["invalid-locale-json"].append((path.name, f"{type(exc).__name__}: {exc}"))
            continue
        missing = set(english) - set(table)
        extra = set(table) - set(english)
        if missing:
            failures["missing-game-keys"].append((path.stem, len(missing), sorted(missing)[:8]))
        if extra:
            advisories["extra-game-keys"].append((path.stem, len(extra), sorted(extra)[:8]))
        for key in set(english) & set(table):
            if not isinstance(table[key], str):
                failures["non-string-value"].append((path.stem, key, type(table[key]).__name__))
            elif tokens(english[key]) != tokens(table[key]):
                advisories["placeholder-difference-candidate"].append((path.stem, key, tokens(english[key]), tokens(table[key])))

    print(f"GAME_LOCALE_AUDIT locales={len(LOCALES)} source_keys={len(english)} item_files={len(item_files)} referenced_keys={len(used)}")
    for category, entries in sorted(failures.items()):
        print(f"FAIL {category}: {len(entries)}")
        for entry in entries[:10]:
            print("  ", entry)
    for category, entries in sorted(advisories.items()):
        print(f"NOTE {category}: {len(entries)}")
        for entry in entries[:8]:
            print("  ", entry)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
