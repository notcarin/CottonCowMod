# Cotton Cow Mod 1.0.2: the cows move into the glade

Hullo, hobbits! 1.0.2 is out, and it's a big bug-fix update. Thank you to everyone who took the time to report problems, here and on Reddit. Every report in this update came from one of you, and several of you described exactly what was going wrong.

**If you've had any trouble with the cows, please update.** You don't need to start a new game, move your decorations, or do anything special: install 1.0.2 and load your save.

## What was wrong, and what changed

### "My cows won't go into the glade"
The cows were always meant to live in the glade (the last garden expansion). In 1.0.1 they spawned near the chicken coop in the front yard instead, with a small roaming area around it. That's why cows were wandering into doors, walls, garden beds and the house, and why widening their roaming range sent them off into the village.

**Now:** the mod works out the shape of your glade, and the cows spawn in the middle of it and only wander inside it. A hungry cow that follows you waits at the edge of the glade instead of trailing you to your front door, and if a cow gets pushed out, she walks back in.

### "I got the 'you need more room' letter, but I have every garden expansion"
The game can record the last garden expansion in two different ways, and 1.0.1 only checked for one of them. On some saves it never noticed the glade was unlocked. Clearing land or moving decorations wouldn't have helped.

**Now:** the mod checks every way the game records it. If this happened to you, your cow arrives the morning after you load your save with 1.0.2.

### "I can't interact with the feeding trough"
Two things could cause this. The trough borrowed its menu from another storage object (like the chicken trough), so if nothing like that was loaded nearby, it simply wouldn't open. Separately, the game has its own Metal Feeding Trough that looks just like ours.

**Now:** the Cow Feeding Trough always opens, and keeps working after you use decoration mode. The letters now name the **Cow Feeding Trough**, so you know which one to place (it's in your Garden Storage). If your Storage is full when the cows arrive, the trough goes to your mailbox instead of being lost.

### "I uninstalled the mod and the Cottons' shops are empty"
This was the worst one, and I'm sorry to everyone it caught. The mod raises the Cottons' friendship cap to 13, and 1.0.1 saved that into your save file. The base game has no shop stock above level 10, so without the mod Farmer Cotton and Young Tom had nothing to sell.

**Now:** saves are written so the base game sees the Cottons at a normal level 10, while the mod keeps their real level safely to the side. Uninstall whenever you like: the Cottons' shops work, and if you reinstall later, your cows and their real levels come back. (The trough disappears while the mod is uninstalled, and you get a new one when you reinstall.)

**If one of your saves already has empty Cotton shops:** install 1.0.2, load that save and sleep once. That fixes the save, and you can uninstall again afterwards if you want.

### Things you might not have noticed
- **Cows vanished every time you loaded your save** and only came back the next morning, and the "here's your cow" letter was sent again. In testing, one save had collected ten copies of the same letter. Now your cows are there as soon as you load, and each letter arrives once.
- **Milk and feeding streaks were shared between all your save slots**, so a new game could start with an old save's milk. Each save now has its own cows. Your existing milk and streak move into your save the first time you load it.
- Milk is no longer lost if your backpack and mailbox are both full; it waits on the cow.
- Saves with the Cottons at level 13 that somehow never got their cow now get it.

## New: a bug report button
If something goes wrong, go to **Settings → Mods** and turn on **Create Bug Report** under Cotton Cow Mod (this needs Tales of the Shire Modding Tools, which the mod already requires). It copies a report to your clipboard, so you can paste it straight into a comment, and also saves it to your Desktop as `CottonCowMod_BugReport.txt`.

The report contains the mod's view of your save (cow and unlock status, the Cotton friendship levels) and recent log lines from the mod. Your Windows user name is removed. It makes it much easier for me to see what's going on without asking you to dig through game folders.

You might also notice the Mods tab is a lot tidier: the mod's developer and internal settings used to show up there, and a couple of them could break a save if switched on. They're hidden now.

## How this update was tested
This is the part I'm most excited about. Instead of me playing through over and over, this update was tested by **automated tests that run inside the real game**. A test version of the mod loads a save, raises the Cotton friendships, unlocks the glade, sleeps through the nights, fills the trough, milks the cows, saves, reloads, and checks every step along the way, with screenshots.

For the uninstall bug, the tests also launch the game **without** the mod and check that the Cottons' shops have stock. Run on an old save from before this update, that check fails exactly the way you reported. Run on the same save after loading it in 1.0.2, it passes.

None of the testing code is in the version you download.

## Known issues
- No custom cow model yet; the cows use the game's own.
- No recipes that use cow milk yet.
- Letters and the trough's text are English only.

## Thank you
Thanks again for the reports, the patience, and the kind words. Several of you said you'd uninstalled while waiting for a fix. I hope 1.0.2 tempts you back to the glade. 🐄

If you run into anything, use **Create Bug Report** and paste the report into a comment or bug report.
