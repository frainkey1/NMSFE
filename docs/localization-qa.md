# Localization QA for NMSFE

NMSFE has two separate localization stores:

- `Resources/ui/lang` contains the editor's interface strings.
- `Resources/json/lang` contains the game's item and discovery strings.

Keep audits separate because they have different key sources and placeholder formats.

## Coverage audits

From the NMSFE folder, run:

```powershell
python .editor_locale_audit.py
python .editor_game_localisation_audit.py
```

The editor audit reads all 15 non-source UI tables recursively, scans nested C# and XAML source, and reports missing/extra keys, duplicate JSON keys, empty/non-string values, placeholder mismatches, likely mojibake, source keys absent from English, and visible literals that are not represented in the English source table. Exact English carryovers are notes for review: names, abbreviations, and accepted technical terms can remain English.

The game audit checks all non-English game-language tables against `en-GB.json`, validates value types and key coverage, compares placeholder tokens, and checks localization references found in the game data JSON files. Placeholder differences and references absent from the English source are review candidates: game-language tables include regional and phrasing differences, so inspect those rows before changing source data.

A clean structural audit confirms keys and placeholders line up. It cannot judge whether a translation reads naturally or whether the UI renders correctly.

## Layout preview with pseudo-localization

Close NMSFE, then start it with the environment variable `NMSFE_PSEUDO_LOCALIZATION=1`. The app uses the English UI strings transformed into visibly accented, expanded text while preserving format placeholders. This is for finding clipping, wrapping, and overflow; it does not modify the translation files or game data. Restart normally to leave pseudo-localization.

For PowerShell, from the NMSFE folder:

```powershell
$env:NMSFE_PSEUDO_LOCALIZATION = "1"
.\Desktop\bin\Debug\net10.0-windows\NMSFE.exe
Remove-Item Env:NMSFE_PSEUDO_LOCALIZATION
```

Use the app's normal language selection to inspect actual translations. Pseudo-localization is a separate layout stress view.

## Capture unresolved visible labels

To collect UI text that the current language table cannot localize, start NMSFE with `NMSFE_LOCALIZATION_AUDIT=1` while using the language being reviewed. The app writes deduplicated JSON Lines to:

`%LOCALAPPDATA%\NMSFE\LocalizationAudit\untranslated-<language>.jsonl`

Rows include the active language, WPF control/property context, visible text, and observation time. This is opt-in and writes only to the user's local app-data folder. It does not record bound input fields, save contents, or change the UI. The report is a candidate list: procedural names and legitimate foreign-language terms may also appear.

## Review sequence

1. Run the structural audits and resolve actual missing keys, malformed files, placeholder mismatches, and encoding damage.
2. Run the UI with `NMSFE_LOCALIZATION_AUDIT=1` in each locale and visit every editor page, tab, menu, nested editor, and dialog. Review that locale's JSONL candidates.
3. Use pseudo-localization to identify layout failures across the same spaces.
4. Reopen the real locale and compare its UI against the English source, paying particular attention to untranslated carryovers and mixed-language labels.
