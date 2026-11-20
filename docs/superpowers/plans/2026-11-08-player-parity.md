# Player functionality restoration

Goal: carry the Player functions shown in screenshots2599–2613 into NMSE.Next WPF without changing the approved stable sidebar/theme architecture.

User authorized implementation and opted out of repeated design gates in established go-mode workflow. Use original NMSE semantics and current source as reference; no feature invented from screenshot text alone.

- [x] Inspect canonical relocated project, original Player controls and headless helpers. Connector checked online.
- [x] General: preserve eight existing Player fields; add metadata, presets/camera, outfit import/export/apply, current coordinates + state/system info, editable coordinates + portal converter/glyphs/roulette, battle timing/trigger. Responsive cards, scrolling, full labels.
- [x] Guide/Titles: account-backed data, descriptive rows/filter/grouping, independent seen/unlocked toggles, bulk operations, preserve unknown IDs.
- [x] Hotkeys/Multiplayer: original action metadata and parameter validation, import/export, experimental opt-in, changed-only save fields.
- [x] Advanced slot utilities: explicit confirmations and verified backup, dirty-state protection, temporary-fixture tests only.
- [x] Integrate navigation/commit/invalidation; test no-op fidelity, validation atomicity, save/account roundtrip, light/dark narrow/wide layouts and retain draft on page changes.
- [x] Build a new preview, inspect captures, launch with real save read-only; hand back conversation front and control released.

Files: replace Desktop/PlayerPage.cs; new PlayerGeneralOperations.cs, PlayerGeneralPage.cs, PlayerKnowledgeOperations.cs/PlayerKnowledgePage.cs, PlayerAdvancedOperations.cs/PlayerAdvancedPages.cs, PlayerSlotOperations.cs/PlayerSaveUtilities.cs; tests corresponding in Checks, integrate Program/PreviewChecks. Original upstream files remain reference.

Review risks: no-op raw token preservation; account and save independent dirty states; missing fields/unknown enum values; coordinate changes must match original relocation semantics; no real-save mutation in tests; source/dest slot collision and recovery; WPF commit after raw structural changes must clear stale references.
