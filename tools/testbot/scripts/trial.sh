#!/bin/bash
# trial.sh <install options...>: installs the randomizer with those options, launches the game and starts a New Game
# (screenshots in $OUT/game/step.png after each key).
REPO=$(cd "$(dirname "$0")/../../.." && pwd)
OUT=${FF12_BOT_OUT:-$REPO/work/bot}
taskkill //IM FFXII_TZA.exe //F >/dev/null 2>&1; sleep 5
# The game's NVIDIA shader cache is mapped into its low memory; at 256 MB it pushes the game's 32-bit buffers past 2 GB
# and the game crashes at boot. Delete it when it gets big.
for c in "$(cygpath "$LOCALAPPDATA")"/NVIDIA/DXCache/fc52a91c35b3d6f9*; do [ -f "$c" ] && [ $(stat -c %s "$c") -ge 100000000 ] && rm -f "$c" && echo "removed big shader cache"; done
"$REPO/app/Ff12Randomizer.Cli/bin/Debug/net10.0-windows/Ff12Randomizer.Cli.exe" install 1 "$@" 2>&1 | tail -1
powershell -ExecutionPolicy Bypass -File "$(cygpath -w "$(dirname "$0")")\game.ps1" -Steps "wait:50,key:w,shot:step,key:enter,wait:8,shot:step,key:enter,wait:40,shot:step" -Dir "$(cygpath -w "$OUT/game")" -Keep 2>&1 | tail -6
