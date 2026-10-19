#!/usr/bin/env bash
# Runs one in-game test suite end to end (Git Bash on Windows).
#
#   Testing/run-tests.sh <suite> <save file name>
#
# 1. Builds the Test configuration (copies the DLL into BepInEx/plugins).
# 2. Backs up the whole TotS save folder to TOTS/SaveBackups/.
# 3. Writes the trigger file and launches the game windowed.
# 4. Waits for the harness to finish (or a timeout), then closes the game.
# 5. Keeps any save the suite created in TOTS/TestFixtures/ (for later runs).
# 6. Restores the save folder from the backup and prints the results.
#
# <save file name> may also name a file in TOTS/TestFixtures/; it is copied into
# the Saves folder for the run.
#
# Turn off Steam Cloud for Tales of the Shire while testing.
# Rebuild with `dotnet build -c Release` before shipping; the Test DLL must never ship.
set -euo pipefail

suite="${1:?usage: run-tests.sh <suite> <save file name>}"
save="${2:-}"
timeout_seconds="${TIMEOUT_SECONDS:-1200}"

repo="$(cd "$(dirname "$0")/.." && pwd)"
game="$(cd "$repo/.." && pwd)"
save_root="$HOME/AppData/LocalLow/WetaWorkshop/TotS"
trigger="$game/BepInEx/config/CottonCowMod.tests.txt"
results="$game/BepInEx/TestResults"
fixtures="$game/TestFixtures"

if tasklist //FI "IMAGENAME eq TotS.exe" | grep -qi "TotS.exe"; then
    echo "Tales of the Shire is already running; close it first." >&2
    exit 1
fi

echo "== Building Test configuration"
(cd "$repo" && dotnet build -c Test -v q -nologo | grep -E "error|warn|Build succeeded" | sort -u)

backup="$game/SaveBackups/$(date +%Y%m%d-%H%M%S)-test-$suite"
echo "== Backing up saves to $backup"
mkdir -p "$backup"
cp -a "$save_root/." "$backup/"

restore() {
    rm -f "$trigger" "$game/steam_appid.txt"
    mkdir -p "$fixtures"
    for f in "$save_root/Saves/"*.tots; do
        [ -e "$f" ] || continue
        name="$(basename "$f")"
        if [ ! -e "$backup/Saves/$name" ]; then
            cp "$f" "$fixtures/$name"
            echo "== Kept new save as fixture: $name"
        fi
    done
    echo "== Restoring saves from backup"
    rm -rf "$save_root/Saves"
    cp -a "$backup/." "$save_root/"
}
trap restore EXIT

if [ -n "$save" ] && [ ! -e "$save_root/Saves/$save" ] && [ -e "$fixtures/$save" ]; then
    cp "$fixtures/$save" "$save_root/Saves/$save"
    echo "== Using fixture save: $save"
fi

rm -rf "$results"
printf 'suite=%s\nsave=%s\n' "$suite" "$save" > "$trigger"

echo "== Launching game (suite: $suite, save: ${save:-none})"
# steam_appid.txt lets the game start directly instead of being relaunched through Steam
# (which asks the user to approve launch options). Removed again in restore().
echo 2016460 > "$game/steam_appid.txt"
(cd "$game" && ./TotS.exe >/dev/null 2>&1 &)

start=$(date +%s)
while true; do
    sleep 5
    elapsed=$(( $(date +%s) - start ))
    if [ -f "$results/status.txt" ]; then
        sleep 5
        break
    fi
    if [ "$elapsed" -gt 60 ] && ! tasklist //FI "IMAGENAME eq TotS.exe" | grep -qi "TotS.exe"; then
        echo "Game exited without finishing the suite." >&2
        break
    fi
    if [ "$elapsed" -gt "$timeout_seconds" ]; then
        echo "Timed out after ${timeout_seconds}s." >&2
        break
    fi
done

if tasklist //FI "IMAGENAME eq TotS.exe" | grep -qi "TotS.exe"; then
    sleep 10
    taskkill //IM TotS.exe //F >/dev/null 2>&1 || true
fi

echo "== Results"
cat "$results/results.txt" 2>/dev/null || echo "(no results.txt)"
echo "== Status: $(cat "$results/status.txt" 2>/dev/null || echo NONE)"
ls "$results" 2>/dev/null | grep -E "\.png$" | sed 's/^/screenshot: /' || true
