# Construction and Alliance Pages Implementation Plan

> **For agentic workers:** Use native execution in this session. Complete each task and its verification before starting the next.

**Goal:** Add a dedicated, save-backed Construction page for owned space stations and an Alliance page placed between Settlements and ByteBeats, with leaderboard values read-only and sourced only from an official interface.

**Architecture:** Keep station records in the existing player-save model, but route them through a dedicated Construction view rather than the planetary Bases section. Add a separate Alliance view that presents official live data when an official interface exists and an explicit no-data state otherwise; never synthesize ranks or scores.

**Tech Stack:** .NET 10, WPF, C#, existing NMSE save model and Checks console project.

**Spec:** docs/superpowers/specs/2026-11-13-construction-and-alliance-design.md

## Global Constraints

- Use only fields present in the loaded save for station edits; apply changes in memory until the standard Save action.
- Use only an official, documented Hello Games interface for live Alliance data.
- Make all leaderboard ranks and scores non-editable.
- Do not use private endpoint scraping, token extraction, or platform-account credential handling.
- Keep missing online data explicit; never invent or hardcode leaderboard values.
- Preserve light and dark theme styling.
- Keep Construction after Bases & Storage and before Catalogue.
- Keep Alliance between Settlements and ByteBeats.

## Review Focus

- Main navigation indices and route selection after inserting two pages — check all page labels route to the intended page and utility view.
- A base list containing both planetary bases and stations — check only SpaceStation entries appear under Construction while other types remain under Bases & Storage.
- A station record with no custom station name — check system discovery names are not substituted for its station/base name.
- Missing, malformed, or unsupported Alliance data — check the page says data is unavailable and shows no fabricated ranking values.
- Leaderboard values supplied by an official source — check they render as display-only fields with no edit path.

---

### Task 1: Stable page routing and navigation order

**Files:**
- Create: Engine/MainPageCatalog.cs (named page indices and ordered page names shared by the engine section catalog, WPF, and Checks)
- Modify: Engine/SectionCatalog.cs
- Modify: Desktop/MainWindow.xaml.cs
- Modify: Desktop/SidebarNavigation.cs
- Modify: Desktop/DomainPages.cs
- Modify: Desktop/DomainPageView.cs
- Modify: Desktop/UtilityPages.cs
- Modify: Desktop/PreviewChecks.cs
- Test: Checks/SectionCatalogChecks.cs

**Interfaces:**
- Produces: MainPageCatalog constants for Construction, Catalogue, Milestones, Settlements, Alliance, ByteBeats, Account Rewards, Export Settings, Raw JSON Editor, and Database Search; its ordered Names are the single source used by navigation.

- [ ] **Step 1: Add page-order checks** for Construction after Bases & Storage, Alliance after Settlements, ByteBeats after Alliance, and the preserved order of Export Settings, Raw JSON Editor, and Database Search.
- [ ] **Step 2: Run the existing Checks command** and confirm the new ordering check fails before the route change.
- [ ] **Step 3: Define MainPageCatalog** and update page order, navigation expansion placement, ShowDomainPage, ShowUtilityPage, and page-specific checks in DomainPageView to use named identifiers or section paths. Preserve the ten-page collapsed sidebar behavior.
- [ ] **Step 4: Run Checks and PreviewChecks**; confirm section indices resolve to intended routes, navigation contains 18 main pages, and More still expands pages after the first ten.
- [ ] **Step 5: Build the WPF project** to catch XAML and route integration errors.

### Task 2: Save-backed station Construction page

**Files:**
- Modify: Engine/SectionCatalog.cs
- Modify: Desktop/DomainPageView.cs
- Modify: Desktop/DomainPages.cs
- Test: Checks/SectionCatalogChecks.cs

**Interfaces:**
- Consumes: named Construction page identifier from Task 1.
- Produces: SectionCatalog.IsSpaceStationRecord(JsonObject baseData) and a Construction section backed by PlayerStateData.PersistentPlayerBases, containing only PlayerSpaceStationBase records.

- [ ] **Step 1: Add fixtures** with HomePlanetBase, PlayerSpaceStationBase, and PlayerSpaceBase records; assert IsSpaceStationRecord selects only PlayerSpaceStationBase.
- [ ] **Step 2: Run Checks** and confirm the filter assertions fail before implementation.
- [ ] **Step 3: Add SectionCatalog.IsSpaceStationRecord(JsonObject baseData)** and the new Construction SectionSpec.
- [ ] **Step 4: Filter DomainPageView entries** so the Bases page omits PlayerSpaceStationBase and Construction displays only those records. Retain original array indices and JSON paths for edits and redirects.
- [ ] **Step 5: Add the Construction page title and detail layout** using the existing record editor for station identity and existing Objects construction data; do not substitute a system discovery name for the station Name field.
- [ ] **Step 6: Run Checks and build**; confirm station data remains attached to the original player save paths and all other base kinds remain visible.

### Task 3: Alliance page with official-source/read-only data boundary

**Files:**
- Create: Engine/AlliancePageData.cs
- Create: Desktop/AlliancePageView.cs
- Modify: Desktop/DomainPages.cs
- Modify: Desktop/MainWindow.xaml.cs (only if the page host needs page-specific lifecycle or commit handling)
- Create: Checks/AllianceChecks.cs
- Modify: Checks/Program.cs
- Modify: Desktop/PreviewChecks.cs

**Interfaces:**
- Consumes: named Alliance page identifier and page host lifecycle from Task 1.
- Produces: immutable AllianceLeaderboardRow(int Rank, string AllianceName, long Score), AlliancePageData(string Status, IReadOnlyList<AllianceLeaderboardRow> Rankings), and AlliancePageData.Unavailable() from Engine/AlliancePageData.cs, plus AlliancePageView(AlliancePageData data). Current official materials expose authenticated Hello Games Cloud access but no documented public leaderboard API; this implementation therefore shows a source-unavailable status and no ranking rows.

- [ ] **Step 1: Add AllianceChecks** asserting that AlliancePageData.Unavailable() returns an explanatory status with zero rows and that AllianceLeaderboardRow exposes no public setters.
- [ ] **Step 2: Run Checks** and confirm the source-state assertion fails before the Alliance view/model exists.
- [ ] **Step 3: Implement AlliancePageData.Unavailable()** and immutable leaderboard rows in Engine/AlliancePageData.cs.
- [ ] **Step 4: Build AlliancePageView(AlliancePageData data)** to echo the supplied profile/stat/leaderboard structure, show the official-source unavailable status, and bind rows to a WPF DataGrid with IsReadOnly=true and CanUserAddRows=false. Do not fabricate profile or ranking values.
- [ ] **Step 5: Add a PreviewChecks assertion** that the Alliance DataGrid is read-only and cannot add rows, even when a fixture row is supplied to the view.
- [ ] **Step 6: Run AllianceChecks and PreviewChecks**, then build and inspect Alliance in the WPF preview in both themes.

### Task 4: Preview and final verification

**Files:**
- Create: next available Build/PreviewNN
- Verify: current save opened read-only in the UI.

- [ ] **Step 1: Find the next unused numbered Preview folder** without replacing earlier previews.
- [ ] **Step 2: Build** with dotnet build Desktop/Desktop.csproj -o Build/PreviewNN.
- [ ] **Step 3: Open the preview and verify** Construction station filtering/details, Alliance placement and unavailable/live state, both themes, and existing page navigation.
- [ ] **Step 4: Confirm the original save remains unchanged** and report the executable path, verified behavior, and any official-service limitation.
