# Base NPC controls design

**Intent:** Restore the legacy per-base controls in NMSE Next so users can inspect and edit hired base workers in the context of the selected base.

**Chosen approach:** Place a Base NPC card in the selected base’s details. It will provide a selector for existing hired workers (Armorer, Farmer, Overseer, Technician, Scientist), race selection, editable seed, seed generation, and “Summon NPC to Base.” This follows the legacy layout and makes the selected base the unambiguous summon destination. The separate Base workers section remains available.

**Behavior and constraints:** Race edits update the worker’s existing `ResourceElement.Filename`; seed edits update the existing second seed element. Summoning updates `BaseUA`, `BaseOffset` (base position extended to four values), and `FreighterBase` on the selected worker. Changes stay in the loaded save’s in-memory state until the normal Save action. Only existing hired workers are selectable; this feature will not create workers or change base objects.

**Alternative considered:** Keep NPC settings in the separate Base workers section and require an additional base selector for summoning. The inline card is chosen because the base is already selected there.

**Failure handling:** If the save has no readable hired workers, show an empty/disabled state instead of creating NPC data. Invalid seed text must not be applied.