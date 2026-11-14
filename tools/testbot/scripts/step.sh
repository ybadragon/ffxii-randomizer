#!/bin/bash
# step.sh [key[+key]/ms]: one input (optional), then one screenshot to $OUT/game/step.png and the position, map and story.
# One key or chord at a time, then look at the screenshot before deciding the next one.
REPO=$(cd "$(dirname "$0")/../../.." && pwd)
OUT=${FF12_BOT_OUT:-$REPO/work/bot}
M=$REPO/tools/testbot/bin/Release/net10.0-windows/memtool.exe
steps="shot:step"; [ -n "$1" ] && steps="key:$1,wait:${2:-1},shot:step"
powershell -ExecutionPolicy Bypass -File "$(cygpath -w "$(dirname "$0")")\game.ps1" -Steps "$steps" -Dir "$(cygpath -w "$OUT/game")" -Keep 2>&1 | grep -v focused
"$M" where 2>&1 | tr -d '\r' | head -1
