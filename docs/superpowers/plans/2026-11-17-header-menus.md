# Header Menus and Commands Implementation Plan

> **For agentic workers:** Execute task-by-task in this session and track each task with the checkboxes below.

**Goal:** Add the centered File, Edit, Tools, Language, and Help menus from the approved design and wire their actions into NMSFE.

**Architecture:** Keep the header layout in MainWindow.xaml and put menu event handlers and state management in focused partial-class files under Desktop. Reuse SaveSession save/load safeguards and the existing engine bulk operations, adding public SaveSession entry points where the current assembly boundary requires them. Keep Help destinations unconfigured until the user supplies them.

**Tech Stack:** C#, WPF/XAML, .NET, existing NMSE.Core and NMSE.Next.Engine components.

**Spec:** [../specs/2026-11-17-header-menus-design.md](../specs/2026-11-17-header-menus-design.md)

## Global Constraints

- Center File, Edit, Tools, Language, Help between the existing title and Light / Dark control.
- Keep the existing Light / Dark toggle and window controls.
- Honor the existing dirty-session confirmation and on-disk hash checks.
- Bulk actions modify in-memory data only; Save writes to disk.
- Do not enable Help links or infer user/personal details.
- Keep platform-specific unsupported operations unavailable or explain why they cannot run.
- Do not add or run tests; verify the product build instead.

## Review Focus

- A loaded save changing on disk while open must still be protected by the existing hash guard on Save.
- A watcher event during unsaved edits must preserve those edits and explain the conflict.
- Restore Backup All must treat the save and paired account files as one transaction.
- Save As must not overwrite an existing file and must activate the verified new copy.
- Language selection must preserve English fallback for missing catalog entries.

---

### Task 1: Centered header menus

**Files:**
- Modify: `NMSFE/Desktop/MainWindow.xaml`
- Modify: `NMSFE/Desktop/MainWindow.xaml.cs`
- Create: `NMSFE/Desktop/MainWindow.Menus.cs`

**Interfaces:**
- Produces: menu initialization and command handlers on `MainWindow`; keep XAML event names consistent with handler names.
- Consumes: existing `OpenFile`, `LoadFile`, `SaveCurrent`, `ToggleTheme`, `Minimize`, `Maximize`, and `CloseWindow`.

- [ ] Replace the two-sided header DockPanel with a left/center/right layout; preserve branding and the existing right controls.
- [ ] Add WPF menus with theme-aware styling and screenshot ordering.
- [ ] Wire File menu existing commands and add Ctrl+O, Ctrl+L, Ctrl+S, Ctrl+Shift+S, F5, Alt+F4 input bindings.

### Task 2: File, reload, backup, and watcher actions

**Files:**
- Modify: `NMSFE/Desktop/MainWindow.Menus.cs`
- Modify: `NMSFE/Engine/SaveOperations.cs`
- Modify: `NMSFE/Engine/SaveSession.cs`

**Interfaces:**
- Produces: `OpenSaveDirectory()`, `LoadSaveDialog()`, `SaveAsCopy()`, `ReloadCurrent()`, `RestoreLatestBackup()`, `RestoreSingleBackup()`, `SetExternalChangeMode(string mode)`, and watcher lifecycle methods.
- Consumes: `SaveSession.Save()`, `SaveSession.SaveCopy(string path)`, `LoadPath(...)`, and `ConfirmDiscard()`.

- [ ] Implement folder selection and save-file filtering while preserving the app's existing discovered choices.
- [ ] Make Save As write a verified new file without overwrite, then switch the active session and selector to that file.
- [ ] Enumerate NMSFE transaction backups and restore either the newest complete transaction or one selected member with confirmation and rollback on failure.
- [ ] Attach a debounced watcher to supported local save paths; default to Ask, and handle Auto/Ignore as defined in the spec.
- [ ] Detach watchers when the active save changes and when the window closes.

### Task 3: Tools, language, and Help actions

**Files:**
- Modify: `NMSFE/Desktop/MainWindow.Menus.cs`
- Modify: `NMSFE/Engine/SaveSession.cs`
- Modify: `NMSFE/Engine/Upstream/Data/UiStrings.cs` only if existing public methods cannot support selection/persistence cleanly.

**Interfaces:**
- Produces: export/import handlers, four SaveSession bulk-operation entry points, persisted language selection, and a Help menu with disabled unconfigured destinations.
- Consumes: `InventoryBulkActions` implementations, `UiStrings`, existing save parser/loader, and current session dirty tracking.

- [ ] Add safe public SaveSession wrappers for recharge, refill, repair slots, and repair technology; return affected counts and mark Dirty only when changed.
- [ ] Export the loaded save as readable JSON; import JSON through the guarded existing loader.
- [ ] Populate Language from `LocalisationService.SupportedLanguages`, load the matching `Resources/ui/lang` catalog, check the active item, and persist the selection.
- [ ] Add Help entries from the screenshot; leave external destinations disabled until provided and show generic NMSFE About information only.

### Task 4: Build and review

**Files:**
- Build output: next unused `NMSFE/Build/PreviewN` directory.

- [ ] Build the WPF app into the next unused preview directory.
- [ ] Review the changed files against every approved design section and report any limitation, especially incomplete localization on hard-coded controls.
