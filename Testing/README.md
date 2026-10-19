# In-game tests

Automated end-to-end tests that run inside the real game. A test build of the mod loads a
save, drives the game through its own code (level-ups, unlock requests, day transitions,
saving and loading), checks the results, takes screenshots, and quits.

Nothing here ships: `Testing/**` only compiles in the `Test` configuration, and the probe is
a separate plugin. **Always rebuild with `dotnet build -c Release` before packaging.**

## Before running
- Turn off Steam Cloud for Tales of the Shire (Properties → General). Tests create saves;
  the scripts restore the save folder afterwards, but Cloud could upload them first.
- Steam must be running. Don't click into the game window during a run.
- Every run backs up the whole save folder to `TOTS/SaveBackups/` first and restores it
  afterwards, even if the run fails.

## Running
From the repo folder, in Git Bash:

```bash
Testing/run-tests.sh <suite> "<save file name>"
Testing/run-uninstall-test.sh "<fixture save name>"
```

Results land in `TOTS/BepInEx/TestResults/`: `results.txt` (every check), `state*.txt`
(the bug-report summary at points in the run), and screenshots.

| Suite | Save to use | Checks |
|---|---|---|
| `smoke` | any | Game loads the save, mod initialises, trough UI found, Create Bug Report (Desktop file, clipboard, user folder scrubbed) |
| `lifecycle` | a save from before the cows (no level 13, no glade) | Level 13 unlock, need-space letters, glade unlock, delivery next morning (one letter each, one trough), cows spawn and stay in the glade, feeding and milking, relationship save record is vanilla-safe; saves as "Cotton Cow Test" |
| `reload` | "Cotton Cow Test" (from `lifecycle`) | In a fresh launch: cows there straight away, no repeat letters, milk and Cotton level 13 kept, cows stay in the glade, feeding still works |
| `upgrade` | a save made with 1.0.0/1.0.1 with cows | Migration: delivered cows recognised, spawn on load, no repeat letters; sleeps and saves as "Cotton Cow Upgraded" |

Saves a suite creates are kept in `TOTS/TestFixtures/`.
`run-uninstall-test.sh` takes one of those, removes Cotton Cow Mod for the run, and uses the
probe plugin (`Testing/Probe`) to check what the game sees without the mod: Cotton max level
10 and shop stock available at their level. The probe then saves as "Cotton Cow Vanilla", so
`reload` on that save tests reinstalling.

The full set, in order (about an hour):

```bash
Testing/run-tests.sh smoke "<any save>"
Testing/run-tests.sh lifecycle "<save from before the cows>"
Testing/run-tests.sh reload "Cotton Cow Test.tots"
Testing/run-uninstall-test.sh "Cotton Cow Test.tots"
Testing/run-tests.sh reload "Cotton Cow Vanilla.tots"     # reinstall after playing without the mod
Testing/run-uninstall-test.sh "<1.0.1 save with cows>"    # control: expected to FAIL (empty shops)
Testing/run-tests.sh upgrade "<1.0.1 save with cows>"
Testing/run-uninstall-test.sh "Cotton Cow Upgraded.tots"  # the repaired save: expected to pass
dotnet build -c Release                                    # put the release build back
```

## How it works
- `TestRunner.cs` starts when `BepInEx/config/CottonCowMod.tests.txt` exists (the script
  writes it): `save=` and `suite=` lines. It waits for the main menu, loads the save the way
  the game's own BenchmarkTool does, runs the suite, writes results, and quits.
- `TestSuites.cs` holds the suites; `TestHelpers.cs` the game-state helpers.
- The scripts create `steam_appid.txt` for the run so the game starts without Steam asking
  to approve launch options, and remove it afterwards.
