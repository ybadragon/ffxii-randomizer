#!/usr/bin/env bash
# sweep.sh [key items...]: open-world reachability sweep. For each teleport crystal not yet touched (or the ones
# given, key items in hex like 8059), walks there with `memtool trip` (teleporting through touched crystals when
# quicker), touches it, and logs one line per leg to work/bot/sweep.log:
#   LEG <item> <map> <place>: OK | FAILED (<the trip's last words>)
# A failed leg usually means a road the story still keeps closed, or something in the way: look at the screen.
cd "$(dirname "$0")/../../.." || exit 1
M=tools/testbot/bin/Release/net10.0-windows/memtool.exe
LOG=work/bot/sweep.log
mkdir -p work/bot
crystals=$(cd tools/mapdata && dotnet run mapdata.cs -- crystals 2>/dev/null | grep -E '^  0x' | sed -E 's/^  0x([0-9A-F]+): (.*) \((...)_..., 0x([0-9A-F]+)\).*/\1 \4 \2/')
touched() { $M get u16 exe+2045072 31 | awk '$1 > 0 { printf "%X\n", $1 }'; }
want="$*"
echo "== sweep $(date +%H:%M:%S)" | tee -a $LOG
while read -r item map place; do
  [ -z "$item" ] && continue
  # heartbeat: the supervisor touches work/bot/heartbeat; older than 20 minutes means nobody is watching, so stop
  if [ -f work/bot/heartbeat ] && [ $(( $(date +%s) - $(stat -c %Y work/bot/heartbeat) )) -gt 1200 ]; then
    echo "SWEEP STOPPED: no heartbeat for 20 minutes" | tee -a $LOG; break
  fi
  if [ -n "$want" ]; then echo " $want " | grep -qi " $item " || continue; fi
  touched | grep -qx "$item" && { echo "SKIP $item $place (touched)"; continue; }
  echo "== leg to $place (0x$map, crystal 0x$item) $(date +%H:%M:%S)"
  $M trip "$map" 2>&1 | tee work/bot/leg.txt; out=$(tail -1 work/bot/leg.txt)
  # exits the trip had to give up on (a closed road, or floor the map data doesn't connect): worth a look
  grep -a "GAVE UP" work/bot/leg.txt | sed "s/^ */  on the way to $place: /" | tee -a $LOG
  if [ "$($M get u16 exe+20441F0)" != "$((16#$map))" ]; then
    echo "LEG $item 0x$map $place: FAILED ($out)" | tee -a $LOG; continue
  fi
  t=$($M teleport touch 2>&1 | tee /dev/stderr | tail -1)
  if touched | grep -qx "$item"; then echo "LEG $item 0x$map $place: OK" | tee -a $LOG
  else echo "LEG $item 0x$map $place: REACHED, TOUCH FAILED ($t)" | tee -a $LOG; fi
done <<< "$crystals"
echo "SWEEP DONE $(date +%H:%M:%S)" | tee -a $LOG
