# Cotton Cow Mod — Installation Guide

This guide walks you through setting up everything needed to run the Cotton Cow Mod for Tales of the Shire.

## Step 1: Find Your Game Folder

You'll need to know where Tales of the Shire is installed.

**Steam:** Right-click Tales of the Shire in your library → Manage → Browse Local Files

This opens your game folder (typically something like `C:\Program Files\Steam\steamapps\common\Tales of the Shire`). You'll be extracting files here in the next steps.

## Step 2: Install BepInEx 5

BepInEx is the mod framework that allows mods to run. You only need to do this once.

1. Go to the [BepInEx 5 releases page](https://github.com/BepInEx/BepInEx/releases)
2. Download **BepInEx_win_x64** (the x64 version — Tales of the Shire is a 64-bit game)
3. Open the zip file and extract **all contents** directly into your game folder
   - When done correctly, you should see `winhttp.dll` and a `BepInEx` folder sitting next to `Tales of the Shire.exe`
4. Run the game once, then close it
   - This lets BepInEx create its configuration files and folder structure
   - You should now have a `BepInEx/plugins` folder inside your game directory

**If the plugins folder didn't appear**, double-check that you extracted the x64 version and that `winhttp.dll` is in the same folder as the game's .exe file.

## Step 3: Install Tales of the Shire Modding Tools

The Modding Tools mod is required for the Cotton Cow Mod to function.

1. Go to the [Tales of the Shire Modding Tools](https://www.nexusmods.com/talesoftheshirealordoftheringsgame/mods/3) page on Nexus Mods
2. Download the latest version
3. Extract the contents directly into your game folder, just like you did with BepInEx
   - This should add files to `BepInEx/plugins/ModdingTools` and create an `ExtraFiles` folder
   - **Important:** If it asks to overwrite `doorstop_config.ini`, say **Yes** — the Modding Tools version is required for everything to work correctly
4. Run the game once and check Settings for a **Mods** tab
   - If you see it, the Modding Tools are working correctly

**If there's no Mods tab**, open `doorstop_config.ini` in your game folder with a text editor and make sure it contains:
```
dllSearchPathOverride="ExtraFiles"
```

## Step 4: Install the Cotton Cow Mod

1. Download `CottonCowMod.dll` from the Nexus page
2. Create a folder called `CottonCowMod` inside `BepInEx/plugins/`
3. Place `CottonCowMod.dll` into that folder
   - Final path: `BepInEx/plugins/CottonCowMod/CottonCowMod.dll`
4. Launch the game

## Verifying It Works

Once in-game, the mod runs silently in the background. You won't see anything different until you've built your friendship with the Cotton family high enough to trigger the cow unlock.

To check the mod loaded, open Settings → Mods: you should see **Cotton Cow Mod v1.0.2** with a "Create Bug Report" option.

## Reporting a Bug

In Settings → Mods, turn on **Create Bug Report** under Cotton Cow Mod. The report is copied to your clipboard, so you can paste it straight into your comment or bug report, and saved to your Desktop as `CottonCowMod_BugReport.txt`. The option switches itself back off.

## Uninstalling

To remove the Cotton Cow Mod, delete the `BepInEx/plugins/CottonCowMod/` folder. Your saves stay playable: without the mod, Farmer Cotton and Young Tom show as relationship level 10 and their shops work normally. The Cow Feeding Trough disappears while the mod is uninstalled. The cows' state is kept in your save, so if you reinstall later, your cows and the Cottons' real levels come back, and a new trough is put in your Storage.

**If you uninstalled version 1.0.1 or earlier and the Cottons' shops are empty:** install 1.0.2, load that save, and sleep once so the game saves. That fixes the save; you can uninstall again afterwards.

To disable all mods temporarily, rename `winhttp.dll` to something else (like `winhttp.dll.bak`). Rename it back to re-enable mods.

## Troubleshooting

**Game won't launch after installing BepInEx:**
- Make sure you downloaded the x64 version, not x86
- Try removing the BepInEx folder entirely and starting fresh from Step 2

**No Mods tab in Settings:**
- Make sure `doorstop_config.ini` was overwritten by the Modding Tools version
- Check that `ExtraFiles` folder exists in your game directory

**Mod doesn't seem to be doing anything:**
- The cow unlock requires relationship level 13 with Farmer Cotton or Young Tom Cotton — keep having shared meals with them!
- Your cows also need the glade, the last garden expansion (Gardening Club level 5). If you get a letter saying you need more room, unlock the glade; the cow arrives the next morning. You don't need to clear it or move your decorations.

**I can't open the cow trough:**
- Make sure you placed the **Cow Feeding Trough** from Garden Storage. The game's own Metal Feeding Trough looks the same but isn't for cows.

**Still stuck?** Use Settings → Mods → Create Bug Report and paste the report into your bug report on Nexus.

**Using WeMod or other memory injectors:**
- These can conflict with BepInEx and cause occasional crashes. If you experience instability, try disabling other injectors while using mods.
