# Cotton Cow Mod
Adds two player-owned cows as late-game livestock, as well as new relationship levels with Farmer Cotton and Young Tom Cotton.

**New in 1.0.2:** your cows now live in the glade, uninstalling is safe, and lots of fixes. See the Changelog tab for details.

![Two cows walking up to say hello in the glade](https://staticdelivery.nexusmods.com/mods/8013/images/5/5-1792431969-286774484.png)

## Requirements
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases)
- [Tales of the Shire Modding Tools](https://www.nexusmods.com/talesoftheshirealordoftheringsgame/mods/3)

## How It Works
Deepen your friendship with the Cotton family beyond the base game's level cap. At relationship level 13, Farmer Cotton and Young Tom Cotton will each gift you a cow of their own, complete with a letter, with new level-up rewards along the way.

Your cows live in the glade: the last garden expansion, unlocked at Gardening Club level 5. If you've earned a cow before unlocking the glade, you'll receive a hint about what's to come, and your cow arrives the morning after you unlock it. You don't need to clear the glade or move your decorations.

## Caring for Your Cows
When your first cow arrives, a **Cow Feeding Trough** appears in your Garden Storage. Place it in the glade and fill it daily with vegetables, grains, or apples. It looks just like the game's Metal Feeding Trough, but only the Cow Feeding Trough feeds your cows.

![The Cow Feeding Trough filled with apples, carrots, cabbage and pumpkin](https://staticdelivery.nexusmods.com/mods/8013/images/5/5-1792431961-1850308582.png)

Feed your cow consistently and she'll produce milk each morning — look for the indicator above her head. The longer your feeding streak, the better the milk quality. Miss a day and your streak resets. A hungry cow will follow you around the glade until you fill the trough.

If your backpack is full when you collect, the milk is sent to your mailbox instead.

## Installation
1. Install BepInEx 5 and the Tales of the Shire Modding Tools (see Requirements above)
2. Place `CottonCowMod.dll` in `BepInEx/plugins/CottonCowMod/`
3. Launch the game

**Updating:** replace `CottonCowMod.dll` with the new one. Your cows come with you.

## Reporting a Bug
In Settings → Mods, turn on **Create Bug Report** under Cotton Cow Mod. A report is copied to your clipboard, ready to paste into a comment or bug report, and saved to your Desktop as `CottonCowMod_BugReport.txt`. It includes the mod's view of your save and recent log lines, with your Windows user name removed.

## Compatibility
**Saves:** each save slot keeps its own cows, milk and feeding streak.
**Uninstalling:** delete `BepInEx/plugins/CottonCowMod/`. Your save stays playable: without the mod, Farmer Cotton and Young Tom show as level 10 and their shops work normally, and the Cow Feeding Trough disappears. Reinstall and your cows and the Cottons' real levels come back, with a new trough in your Storage.
**Uninstalled an older version and the Cottons' shops are empty?** Install 1.0.2, load that save and sleep once. That fixes the save, and you can uninstall again afterwards.
**WeMod/memory injectors:** Using WeMod or other memory injectors alongside BepInEx mods may cause crashes.

## Known Issues
- No custom cow model yet; the cows use the game's own.
- No recipes that use cow milk yet.
- Letters and the trough's text are English only.

Feedback on gameplay feel, balance, and anything that seems off is very welcome. If something goes wrong, please include a bug report from Settings → Mods.
