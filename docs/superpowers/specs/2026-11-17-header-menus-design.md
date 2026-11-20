# Header Menus and Commands — Design Draft

Date: 2026-11-17  
Status: Awaiting user review

## Goal

Add the File, Edit, Tools, Language, and Help menus shown in the NMSE screenshots to the NMSFE header. Center the menus between the existing “Save File Editor” title and the existing Light / Dark control. Preserve the current Light / Dark control and window buttons.

Wire commands already supported by NMSFE, then implement the additional save, backup, watch, import/export, and bulk inventory commands shown in the screenshots. Leave Help ready for project links and personal information the user will provide later.

## Header layout

Use a three-part header layout: branding on the left, the five menus centered, and Light / Dark plus window controls on the right. Menus follow the screenshot order: File, Edit, Tools, Language, Help. The theme toggle stays on the right; no duplicate Theme menu is added. Menu colors and hover states use the current theme resources. At the existing minimum window width, the center group must remain usable without overlapping the title or window controls.

## Menus and behavior

### File

- **Open Save Directory…** opens a folder picker and updates the save-file choices to that folder.
- **Load Save File…** opens a supported save file and routes through the existing discard-changes guard and loader.
- **Save** invokes the existing verified save path and keeps its current disk-change protection.
- **Save As…** writes a verified copy to a new filename without overwriting an existing file, then makes that copy the active save.
- **Exit** closes the window through its existing close/unsaved-changes flow.
- Include the screenshot shortcuts: Ctrl+O, Ctrl+L, Ctrl+S, Ctrl+Shift+S, and Alt+F4.

### Edit

- **Reload (F5)** reloads the active save through the existing discard-changes guard.
- **Restore Backup (All)** offers the latest complete NMSFE backup transaction for the active save and restores its associated save/account files together after a confirmation.
- **Restore Backup (Single)** lets the user choose one backup file from the active save’s NMSFE backup history and restores only that file after a confirmation.
- **Save File Watching** offers Ask before reloading, Reload automatically, and Ignore changes. Ask is the default. Automatic reload applies only when there are no pending in-memory edits; if edits are pending, preserve them and notify the user. Ignore leaves the open session untouched. The watcher follows the active local save and is detached when another save is loaded or the window closes.

### Tools

- **Export JSON…** exports the loaded save data as readable JSON.
- **Import JSON…** loads a JSON save through the existing save parser, loader, and discard-changes guard.
- **Recharge All Technology**, **Refill All Stacks**, **Repair All Slots**, and **Repair All Technology** call NMSFE’s existing bulk algorithms. They update the in-memory save only, refresh the visible page, mark the session dirty when data changed, and report the number of affected entries. The existing Save command remains responsible for writing changes to disk.

### Language

- List the 16 supported language tags from the existing localization service: en-GB, fr-FR, it-IT, de-DE, es-ES, ru-RU, pl-PL, nl-NL, pt-PT, es-419, pt-BR, zh-CN, zh-TW, ko-KR, ja-JP, and en-US.
- Show a check mark on the selected language and persist the choice so it survives relaunch.
- Load the matching existing UI language catalog. Menus and UI labels already routed through NMSFE’s UiStrings service can use translations immediately. Controls that currently contain hard-coded English text will remain English until they are connected to that service; this menu change will not claim full UI translation.

### Help

Use the screenshot entries: GitHub Page, User Guide, Sponsor Development, Check for Updates…, Release Notes, and About. Keep the link/update entries visible but disabled until you provide the project URLs and update/release source. About can open a simple NMSFE information dialog with no personal details, ready for you to fill in later.

## Safety and state

- Commands that replace the active save or its backup files require a confirmation and honor the existing unsaved-change guard.
- Bulk inventory commands edit only the in-memory session. They do not write to the save until Save is used.
- Save As and backup restore validate the resulting files before activating/replacing them.
- Watcher events are debounced so one game write does not prompt repeatedly.
- Console save formats remain subject to the current platform-specific save support; menu commands must be disabled or explain when the active format cannot use a specific operation.

## Review notes

The existing save engine includes verified Save and SaveCopy operations plus transactional backups under `.nmse-next-backups`. The four bulk algorithms already exist in the Engine assembly. The app includes 16 UI language catalogs and the existing UiStrings service, but much of the WPF screen text is currently hard-coded. The older NMSE source at the supplied location informed the menu labels and supported language list.

No Help links or personal information are assumed in this draft.
