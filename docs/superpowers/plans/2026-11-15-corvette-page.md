# Corvette page implementation plan

## Goal
Add a standalone Corvette page directly below Fleet in the left sidebar. Move Corvette records out of Starships so they appear once, use the existing shared 12 ship slots, mirror the existing Starship details and inventory experience, and include a blank Customisation tab.

## Steps
1. Inspect sidebar/page routing and current Starship and Corvette data handling.
2. Add a new page route and sidebar entry without changing existing page IDs.
3. Filter Corvette records out of Starships and into the new Corvette page; preserve the shared 12-slot limit.
4. Add the Corvette details and Cargo/Technology tabs, assembled-parts JSON access, and an intentionally empty Customisation tab.
5. Build the application and inspect the patch.

## Constraints
- Fleet is only the sidebar location reference; Corvettes are a separate page.
- Do not duplicate Corvette entries in Starships.
- Customisation stays blank.
- Do not run tests unless requested; compile the application to verify the feature.
