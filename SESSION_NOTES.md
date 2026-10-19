# Cotton Cow Mod — Session Notes

## Session: Oct 19, 2026 — v1.0.2 (player bug reports + audit)

### Player reports and causes
- **"Need more garden space" forever**: garden 5 check only looked at `Unlock_Garden_Area_5`; the
  game also/only records `GARDEN_AREA_Unlock_Garden_Area_5`. Now checks both + GardenAreaUnlockController.
- **Cows in the front yard / stuck in doors / roaming the village**: without the dev-only
  CowSpawnPosition, cows spawned at the chicken spawn point. Homestead boundaries are cumulative
  (Area 5 polygon = whole homestead), so the glade = inside Area 5, outside Area 4 → `GladeArea.cs`.
  Polygon transform y is ~0 while the glade ground is ~11, so ground is found by raycast + NavMesh.
  Roaming area = one box per row of 2m glade cells (MapLocationArea clamps to the closest collider).
  Follow targets are inset 1.5m; a leash walks strays back (CowFollowBehavior).
- **Can't open the trough**: UI prefab was only cached from an active PhysicalStorage nearby.
  Now also inactive/prefabs + PhysicalStorage.Awake postfix. Letters name the Cow Feeding Trough.
- **Cotton shops empty after uninstalling**: the save stored Cotton maxLevel 13; vanilla has no
  stock above 10. `RelationshipSavePatch` writes vanilla values and stores real XP as
  `COTTONCOWMOD_XP_<npc>` in RelationshipManager's unlock strings. Broken saves repair on next save.

### Architecture changes
- Per-save state in `SaveState` (`COTTONCOWMOD_STATE_*` strings in UnlockManager): Delivered,
  Pending, HasMilk, MilkQuality, Streak, Version. 1.0.1's config values migrate once (`LegacyConfig`).
- `PlayerCows` waits for "player has control after a load" before evaluating (save systems load in
  no fixed order; the game raises date events while loading). Delivered cows spawn on load, letters
  only on first delivery.
- Dev/debug settings moved to `com.cottoncowmod.tots.dev.cfg` (ModdingTools shows every entry in
  plugin.Config in Settings > Mods). Only "Create Bug Report" is in the main config.
- Version is `CottonCowModPlugin.Version`.

### Testing
- In-game automated tests: see `Testing/README.md`. `dotnet build -c Test` includes the harness;
  always `dotnet build -c Release` before packaging.
- Saves backed up in `TOTS/SaveBackups/`; test saves in `TOTS/TestFixtures/`.

### Other
- Unreleased 1.1.0 work is in a separate local copy (not in this repo). It needs the 1.0.2
  changes merged in before release.
- Release docs (README_NEXUS.md, INSTALL_GUIDE.md) are now in `release/`.
- Not done (low priority): localization of letters/trough text/rejection text; CowDiet is a fixed
  list (modded vegetables excluded); CowTrough inventory is created in every save even before unlock.

## Session: Feb 26, 2026

### What We Accomplished

#### Phase 4: Visual Food Display (`CowTroughFoodDisplay.cs`)
- **Created** dynamic food display system for the cow trough
- Shows 3D food models inside the physical trough based on inventory contents
- Each of the 4 inventory stacks maps to a display zone along the trough's long axis
- **Quantity tiers**: 1 item → 1 model, 2–3 → 2, 4–6 → 3, 7–9 → 4 models per slot
- **Auto-scaling**: measures each food prefab's mesh bounds (all 3 axes) and normalizes to a consistent size relative to slot spacing
- **Organic positioning**: seeded random offsets + rotation (full Y spin, ±15° tilt) so food looks tossed in naturally
- **Alignment fix**: uses mesh bounds transformed into trough-local space instead of world-space renderer bounds, so the long axis detection works regardless of trough rotation
- Added `CowTroughFoodDisplay` component in `TroughPlacementPatch.cs`

#### Rejection Text Fix (`CowTroughRejectionTextOverride.cs`)
- The chicken trough's rejection text ("Must be a Nut, Grain or Berry") is baked into an Animator animation clip
- The TMP_Text component is on a GameObject named `"Text"` in the StorageUIScreen hierarchy — NOT under the `m_TransferIcon` (which is where we originally looked)
- Final approach: find the text by GameObject name once, disable its `LocalizedText`, override each LateUpdate frame
- Overrides with `CowDiet.DietDescription`: "Cows eat vegetables, grains, and apples — not mushrooms"

#### Mail Letter Formatting (`CowMailSender.cs`)
- Fixed missing `+` concatenation operator in Farmer Cotton's letter
- Normalized line lengths across both letters
- "Hullo Hobbit!" is on its own line followed by `\n\n` to match other in-game letter formatting

#### Git Setup
- Initialized repo, pushed to https://github.com/notcarin/CottonCowMod
- Git identity: `notcarin` / `notcarin@users.noreply.github.com` (repo-local config)
- Added `D:/SteamLibrary/steamapps/common/TOTS/CottonCowMod` as a git safe directory (needed because it's on a secondary Steam drive)
- Initial commit includes all 24 source files (3,543 lines)

### Tuning Values (Current)
- `CowTroughFoodDisplay.FoodSizeFraction = 0.5f` — food size as fraction of slot spacing
- `CowTroughFoodDisplay` Y position: `center.y - extents.y * 0.6f` — rests near the bottom of the L-shaped trough
- `CowTroughFoodDisplay.MaxTiltDegrees = 15f` — user liked this amount of tilt
- Auto-scale uses `Max(x, y, z)` of mesh bounds — fixed issue where baby marrow (elongated along Y) was oversized

### Known Issues / Things to Watch
- **No hay fill in trough**: The metal slat trough has no solid bottom, so food appears to float above the gaps. We decided against procedural hay. No existing hay/straw asset found in the game. Lower Y position mostly mitigates this.
- **`\n\n` in mail letters**: Assumed this creates a paragraph break in the mail UI — needs verification in-game
- **XP threshold tuning**: Logging fix was deployed in a prior session (moved before the MaxLevel guard in `LevelCapPatches.cs`). Still awaiting a game run to see L1–L11 thresholds and decide if `XpForLevel11 = 500` is appropriate.
- **Rejection text search**: Currently uses `_searched` bool to find the text once. If for some reason the text component is recreated mid-session, it won't re-find it. Hasn't been an issue in testing.

### Architecture Reminders
- **Inventory stacking**: `Inventory.Items` is a flat list. We manually group by `ItemType` to get stacks (the `ItemIndex.Stacks` is private).
- **Chicken trough visual system**: `StorageVisualAutoPopulation` + `VisualAutoPopulationElement` — purely 1:1 item-to-visual with pre-placed GameObjects. No quantity logic. Not reusable for our runtime trough.
- **`m_TransferIcon`**: Type is `StorageTransferIcon` (MonoBehaviour). Has `m_Anim` (Animator) and `TriggerAnimation(string trigger)`. The rejection text is NOT under this component's hierarchy.
- **Food scale cache**: `_scaleCache` maps food type name → computed scale. Logged on first encounter for each food type.

### Files Modified This Session
- `CowTroughFoodDisplay.cs` — complete rewrite (stack-based display, quantity tiers, organic positioning, auto-scale, mesh-local bounds)
- `CowTroughRejectionTextOverride.cs` — rewritten to find text by GameObject name in full UI hierarchy
- `CowMailSender.cs` — formatting fixes (missing `+`, line lengths, `\n\n` greeting)
- `Patches/TroughPlacementPatch.cs` — added `AddComponent<CowTroughFoodDisplay>()`
- `Patches/StorageUIScreenPatch.cs` — simplified rejection text override setup
- `.gitignore` — created (excludes bin/, obj/, .vs/)

### Pending Work
- **Commit current changes** to GitHub (last commit was the initial push; fixes since then are uncommitted)
- **In-game verification** of `\n\n` mail formatting
- **XP threshold review** once log data is available
- Phase 4 plan doc at `D:\SteamLibrary\steamapps\common\TOTS\PHASE4_FOOD_DISPLAY_PLAN.md` can be archived/deleted
