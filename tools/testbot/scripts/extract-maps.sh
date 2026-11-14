#!/bin/bash
# extract-maps.sh [filter]: rebuilds work/mapres/tex (not in git: game assets). For every map package
# ps2data/plan_master/map_ctrl/<area>/<map>/bin/<map>.mpk it saves the package's whole-map minimap picture (its last
# TIM2 texture at least 512 wide: red floor, cyan walls) as tex/<map>.png. The test bot's mapfind/tnav read these.
# Needs the randomizer CLI (app/Ff12Randomizer.Cli, Debug) and the test bot (tools/testbot, Release) built.
REPO=$(cd "$(dirname "$0")/../../.." && pwd)
CLI=$REPO/app/Ff12Randomizer.Cli/bin/Debug/net10.0-windows/Ff12Randomizer.Cli.exe
M=$REPO/tools/testbot/bin/Release/net10.0-windows/memtool.exe
TEX=$REPO/work/mapres/tex; TMP=$REPO/work/mapres/tmp
mkdir -p "$TEX" "$TMP"
"$CLI" list "plan_master/map_ctrl/${1:-}" </dev/null | grep -o 'ps2data/plan_master/map_ctrl/[^ ]*\.mpk' | sort -u | while read -r path; do
  pkg=$(basename "$path" .mpk)
  [ -f "$TEX/$pkg.png" ] && continue
  "$CLI" extract "$path" "$(cygpath -w "$TMP/$pkg.mpk")" </dev/null >/dev/null
  # memtool tim2 numbers only the pictures it saves (<prefix>_NN.png); pick the last saved one at least 512 wide
  n=-1; i=0; w=0
  while IFS= read -r line; do
    case "$line" in
      "TIM2 at"*) w=$(echo "$line" | sed 's/.*: \([0-9]*\)x.*/\1/'); [ "$w" -ge 512 ] && n=$i; i=$((i + 1)) ;;
      *skipped*) i=$((i - 1)); [ "$n" -eq "$i" ] && n=-1 ;;
    esac
  done < <("$M" tim2 "$(cygpath -w "$TMP/$pkg.mpk")" "$(cygpath -w "$TMP/$pkg")" </dev/null)
  [ "$n" -ge 0 ] && cp "$TMP/${pkg}_$(printf %02d $n).png" "$TEX/$pkg.png" && echo "$pkg"
  rm -f "$TMP/$pkg"*
done
rmdir "$TMP" 2>/dev/null
