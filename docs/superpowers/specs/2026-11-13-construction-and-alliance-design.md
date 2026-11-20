# Construction and Alliance pages — design

Date: 2026-11-13

## Intent
Add the new Cosmos space-station and galactic-alliance features to NMSE Next without misclassifying an owned station as a planetary base or allowing leaderboard manipulation.

## Goals
- Add a **Construction** main page for owned space stations.
- Use the existing PersistentPlayerBases records classified as SpaceStation; keep them separate from ordinary planetary bases.
- Present the station identity and available construction data (interior parts, exterior decorations, rooms) using the save’s existing fields. Edits remain in memory until the standard Save action.
- Add **Alliance** between **Settlements** and **ByteBeats**.
- Follow the supplied in-game references: alliance identity/banner, member count, alliance score and category stats, plus rankings.
- Treat every leaderboard rank and score as read-only.
- Use live alliance data only through an official, documented Hello Games interface. If no such interface is available, report that the live data cannot be loaded instead of inventing values.

## Non-goals
- No private endpoint scraping, token extraction, or platform-account credential handling.
- No fabricated or hardcoded leaderboard rows.
- No edits to online leaderboard scores, ranks, or server-calculated stats.

## Constraints and current structure
- The main navigation is an indexed _pages array in Desktop/MainWindow.xaml.cs.
- SectionCatalog.Get(7) currently includes PersistentPlayerBases; BaseListLogic can classify SpaceStation records.
- The general domain view edits existing save fields, and the Save workflow stages and verifies local save data.
- The official Cosmos materials describe station ownership/construction and alliance rankings; Hello Games Cloud requires an authenticated platform account for alliance access. No public Alliance API was found in the official materials during initial research.
- UI must follow NMSE Next's existing light/dark theme and screenshot conventions.

## Approaches considered
1. **Dedicated save-backed Construction + official-source Alliance (selected):** isolate station records in Construction; put Alliance in the requested nav position; show editable fields only where a supported local/official data contract allows them; display live rank/score values as read-only.
2. **Keep stations in Bases & Storage and add only an Alliance page:** less code, but continues to mix a space station with planetary bases and does not meet the requested new main page.
3. **Use a private online endpoint or static screenshot values:** could appear live or complete, but risks account security, breaks when the service changes, or presents stale/fabricated scores. Rejected.

## Behavior when live Alliance data is unavailable
Construction remains usable from the loaded save. Alliance shows a clear connection/source status and does not display ranking numbers until a documented official source can be used. The page layout may still show local alliance fields if the loaded files contain a supported record.

## Builder calls
- Place Construction after Bases & Storage, before Catalogue, as a station/building section.
- Move only SpaceStation entries out of the planetary-base list; preserve other base kinds.
- Keep Alliance between Settlements and ByteBeats.
- Preserve names and values as stored; do not turn system names into station names.
- Reuse existing JSON navigation redirects for the corresponding save-backed station records.
