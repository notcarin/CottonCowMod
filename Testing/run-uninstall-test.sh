#!/usr/bin/env bash
# Checks a save stays healthy when Cotton Cow Mod is uninstalled (Git Bash on Windows).
#
#   Testing/run-uninstall-test.sh <fixture save name>
#
# Uses a save from TOTS/TestFixtures/ (e.g. one the lifecycle suite created), then:
# 1. Backs up the save folder, copies the fixture in.
# 2. Moves CottonCowMod.dll out of BepInEx/plugins and puts the test probe in its place.
# 3. Launches the game; the probe loads the save and reports the Cotton relationship
#    levels and whether their shops have stock, as vanilla sees them.
# 4. The probe saves as "Cotton Cow Vanilla" (kept in TestFixtures) so a reinstall can be
#    tested with: Testing/run-tests.sh reload "Cotton Cow Vanilla.tots"
# 5. Puts the mod back, removes the probe, restores saves, prints the results.
#
# Turn off Steam Cloud for Tales of the Shire while testing.
set -euo pipefail

save="${1:?usage: run-uninstall-test.sh <fixture save name>}"
timeout_seconds="${TIMEOUT_SECONDS:-900}"

repo="$(cd "$(dirname "$0")/.." && pwd)"
game="$(cd "$repo/.." && pwd)"
save_root="$HOME/AppData/LocalLow/WetaWorkshop/TotS"
plugins="$game/BepInEx/plugins"
trigger="$game/BepInEx/config/CottonCowMod.probe.txt"
results="$game/BepInEx/TestResults"
fixture="$game/TestFixtures/$save"
parked="$game/TestFixtures/parked-plugin"

[ -e "$fixture" ] || { echo "No fixture at $fixture" >&2; exit 1; }
if tasklist //FI "IMAGENAME eq TotS.exe" | grep -qi "TotS.exe"; then
    echo "Tales of the Shire is already running; close it first." >&2
    exit 1
fi

echo "== Building probe"
(cd "$repo/Testing/Probe" && dotnet build -c Release -v q -nologo | grep -E "error|warn|Build succeeded" | sort -u)

backup="$game/SaveBackups/$(date +%Y%m%d-%H%M%S)-uninstall-test"
echo "== Backing up saves to $backup"
mkdir -p "$backup" "$parked"
cp -a "$save_root/." "$backup/"

restore() {
    rm -f "$trigger" "$plugins/CottonCowTestProbe.dll" "$game/steam_appid.txt"
    if [ -e "$save_root/Saves/Cotton Cow Vanilla.tots" ]; then
        cp "$save_root/Saves/Cotton Cow Vanilla.tots" "$game/TestFixtures/"
        echo "== Kept the save the game wrote without the mod: Cotton Cow Vanilla.tots"
    fi
    if [ -e "$parked/CottonCowMod.dll" ]; then
        mv "$parked/CottonCowMod.dll" "$plugins/CottonCowMod.dll"
        echo "== Cotton Cow Mod put back"
    fi
    echo "== Restoring saves from backup"
    rm -rf "$save_root/Saves"
    cp -a "$backup/." "$save_root/"
}
trap restore EXIT

echo "== Uninstalling Cotton Cow Mod for this run"
mv "$plugins/CottonCowMod.dll" "$parked/CottonCowMod.dll"
cp "$repo/Testing/Probe/bin/Release/CottonCowTestProbe.dll" "$plugins/"
cp "$fixture" "$save_root/Saves/$save"

rm -rf "$results"
printf 'save=%s\n' "$save" > "$trigger"

echo "== Launching game without the mod (save: $save)"
# steam_appid.txt lets the game start directly instead of being relaunched through Steam
# (which asks the user to approve launch options). Removed again in restore().
echo 2016460 > "$game/steam_appid.txt"
(cd "$game" && ./TotS.exe >/dev/null 2>&1 &)

start=$(date +%s)
while true; do
    sleep 5
    elapsed=$(( $(date +%s) - start ))
    [ -f "$results/status.txt" ] && { sleep 5; break; }
    if [ "$elapsed" -gt 60 ] && ! tasklist //FI "IMAGENAME eq TotS.exe" | grep -qi "TotS.exe"; then
        echo "Game exited without finishing." >&2
        break
    fi
    [ "$elapsed" -gt "$timeout_seconds" ] && { echo "Timed out." >&2; break; }
done

if tasklist //FI "IMAGENAME eq TotS.exe" | grep -qi "TotS.exe"; then
    sleep 10
    taskkill //IM TotS.exe //F >/dev/null 2>&1 || true
    sleep 3
fi

echo "== Results"
cat "$results/probe.txt" 2>/dev/null || echo "(no probe.txt)"
echo "== Status: $(cat "$results/status.txt" 2>/dev/null || echo NONE)"
