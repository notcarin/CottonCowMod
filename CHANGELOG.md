# Changelog

## 1.0.2

### Fixes
- **Cows now live in the glade.** They spawn in the middle of the glade (the last garden
  expansion) and only wander inside it, instead of spawning in the front yard and getting
  stuck in doors, walls, garden beds and the house. A hungry cow following you waits at the
  edge of the glade, and a cow that gets pushed out walks back in. Your decorations and
  garden beds don't need to move.
- **Cows arrive even if the mod couldn't see your last garden expansion.** Some saves
  record the expansion in a way 1.0.1 didn't check, so players got the "you need more room"
  letter forever. If this happened to you, your cow arrives the morning after you load
  your save with 1.0.2.
- **Uninstalling the mod no longer empties the Cottons' shops.** Saves are now written
  so the game without the mod sees normal Cotton relationship levels. If your save already
  has this problem: install 1.0.2, load the save, sleep once (so the game saves), and the
  save is fixed. You can uninstall afterwards.
- **The cow trough always opens.** It used to need another storage (like the chicken
  trough) loaded nearby. It also keeps working after decoration mode, and if your Storage
  is full when you get the trough, it's sent to your mailbox instead of being lost.
- **A missing Cow Feeding Trough is replaced.** The game drops the trough if you save while
  the mod is uninstalled; when you reinstall, you get a new one in your Storage.
- **Cows no longer vanish until morning every time you load**, and the "here's your cow"
  letter is no longer sent again.
- **Milk and feeding streaks are saved per save slot.** Each save now has its own cows'
  state, a new game starts fresh, and reloading no longer gives extra milk. Existing milk
  and streaks from 1.0.1 are moved into your save the first time you load it.
- Milk is no longer lost when your backpack and mailbox are both full; it waits on the cow.
- Cotton relationship level 13 saves that never got their cow now get it.

### Changes
- **New: Create Bug Report.** In Settings → Mods (needs ModdingTools), turn on
  "Create Bug Report". A report is copied to your clipboard (paste it into your bug report)
  and saved to your Desktop as `CottonCowMod_BugReport.txt`. It includes the mod's view of
  your save and recent log lines, with your Windows user name removed.
- The Mods tab no longer shows the mod's developer and internal settings. They were never
  meant for players, and some could break a save.
- The cow letters now name the **Cow Feeding Trough** (it looks like the game's Metal
  Feeding Trough, which cows don't use).
