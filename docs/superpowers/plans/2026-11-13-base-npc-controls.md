# Base NPC controls implementation plan

> **For agentic workers:** Follow the selected execution method after plan review. The user approved the design and selected inline placement.

**Goal:** Restore per-base selection and editing of existing hired NPC workers in the Bases detail view.

**Architecture:** Add a small Engine helper for hired-worker/race data and safe summon writes. Add an inline WPF card in the selected base details; race and seed drafts use the existing `FieldEdit` / `SaveSession.ApplyFields` path, and summoning uses one validated `SaveSession` operation.

**Tech Stack:** C# 10, .NET 10, WPF, existing `NMSE.Models.JsonObject/JsonArray` and `SaveSession`.

**Spec:** `docs/superpowers/specs/2026-11-13-base-npc-controls-design.md`

## Global Constraints

- Only existing hired workers are selectable; do not create worker or base records.
- Keep edits in memory until the normal Save action.
- Race edits target `ResourceElement.Filename`; seed edits target the existing second element of `ResourceElement.Seed`.
- Summon writes `BaseUA`, four-value `BaseOffset`, and `FreighterBase`.
- Do not add or run tests unless requested; verify with a build.

## Review Focus

- Missing/unreadable `NPCWorkers`: show no selectable worker and do not create data.
- Unhired specialist entries: omit them from the picker.
- Unknown race filename: leave selection neutral; do not rewrite it until the user selects a race.
- Missing/malformed seed array: disable seed editing/generation for that worker.
- Base without usable address or three-position vector: reject summon without partial writes.

---

### Task 1: Engine NPC data and summon operation

**Files:**
- Create: `Engine/BaseNpcOperations.cs`

**Interfaces:**
- Provide worker entries with save-array index, specialist label, and attached JSON object.
- Provide the legacy race labels/resource filenames and filename-to-label lookup.
- Add `SaveSession.SummonBaseNpc(JsonObject worker, JsonObject baseRecord)`; validate both records are attached to the loaded save and the target address/position are usable before writing all three summon fields and marking the session dirty.

- [ ] Implement the helper API and the all-or-nothing summon mutation.
- [ ] Review the stated missing-data and malformed-position behavior.

### Task 2: Inline Base NPC card

**Files:**
- Modify: `Desktop/DomainPageView.cs`

**Interfaces:**
- Show the card only for a selected readable base record on Bases & Storage > Bases.
- Use the Engine helper’s worker/race choices.
- Race and seed edits use existing `FieldEdit` / `SaveSession.ApplyFields`, including the existing Apply changes action.
- Include worker selector, race selector, editable seed, Generate, and confirmation-backed Summon NPC to Base.

- [ ] Add and wire the card without changing the Construction page or the separate Base workers raw section.
- [ ] Verify the desktop application builds to `Build/Preview40` with no warnings or errors.

---

The two tasks are deliberately split at the Engine/UI boundary: the UI should not duplicate save-structure rules, while the Engine helper should not depend on WPF.
