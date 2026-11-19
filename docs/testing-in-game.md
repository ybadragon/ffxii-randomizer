# Testing in game

How to check a change in the running game, by hand or with the test bot in `tools/testbot`. The manual checklist
for each feature is in [test-checklist.md](test-checklist.md).

## Setup

- Run the game windowed at 1280x720, so screenshots and the minimap crop line up. The bot reads the minimap at
  window offset (1260, 30), 280x280.
- Back up the save folder before automating anything. A New Game autosaves into the first slot. The bot saves only to
  slots 006 and 007; slots 001-005 are the player's.
- Controllers need Steam Input turned on for this game.
- If the game crashes at boot even unmodified, delete the big NVIDIA shader cache
  ([game internals](game-internals.md#crashes-and-hangs)).

## Installing a test setup

`Ff12Randomizer.Cli install 1 <options...>` installs seed 1 with the given options and doesn't touch History. When
you're done, put back the user's own seed or the options they asked for. Test switches (CLI only):

| Switch | Effect |
| --- | --- |
| `exp_freeroam` | New Game skips the story: full party, late story flag, starts in Rabanastre |
| `exp_flag=<hex>` | The story flag to start with (with `exp_freeroam`) |
| `exp_map=<hex>`, `exp_entry=<n>` | Start map and entrance (map ids in `Core/Data/mapAreas.csv`) |
| `exp_level=99` | Party starts at that level |
| `exp_weak=<text>` | Monsters in area files whose path contains the text have 1 HP (e.g. `dst`) |
| `exp_give=<hex id>:<count>,...` | Vaan starts with these items (e.g. Teleport Stones) |
| `exp_noflag`, `exp_noparty`, `exp_norestore`, `exp_sewer`, `exp_switch` | Turn off parts of `exp_freeroam` (see `SkipPrologueFeature.cs`) |

For example, `install 1 exp_freeroam exp_flag=01A4 exp_map=131 exp_level=99 exp_weak=dst openworld` starts at
Rabanastre's Eastgate at story 420, with weak Estersand monsters and our open world.

## Driving the game: one step at a time

`tools/testbot/scripts/step.sh [key[+key]/ms]` presses one key or chord (optionally held for ms milliseconds). It then
saves a screenshot to `work/bot/game/step.png` and prints the map, story flag, position and camera.
`trial.sh <options>` installs, launches and starts a New Game.

The loop: screenshot, press **one** key or chord, screenshot, **look at it**, decide the next key. Don't chain several
keys in one command and judge the result from memory values afterwards. Chained runs hid dialogs and wall bumps, and
they wasted whole runs.

- **Look at the screenshot before concluding anything.** A position that doesn't change can mean a dialog waiting for
  Space, a battle, a town map, or a real soft-lock. Only the picture tells them apart.
- Start a New Game without fixed long waits: press the next key as soon as the screen is ready (well under a minute).
- Delete screenshots once you've looked at them, and delete crash dumps. Don't leave storage used.

Controls:

| Key | Does |
| --- | --- |
| W A S D | Move, relative to the camera |
| Arrow keys | Rotate the camera |
| Space | Talks or interacts, only when the "Space &lt;name&gt;" prompt shows; also confirms dialogs |
| Enter, Space or F with no prompt | Opens the battle menu (C closes it) |
| M | Full map (explored parts only; 2 zooms) |
| Escape | Pause (C = back, F = exit to title, R = quit game) |
| Left Ctrl (held) | Flee (R2): "FLEEING" shows and the party stops fighting. The bot holds it while walking |
| Left Shift | Walk mode |
| K | D-pad down (moves the "Select Leader" cursor, where S doesn't) |
| Enter | Advances some battle messages ("All members of the party have fallen") where Space opens the pause screen |

The bindings are in `Documents\My Games\FINAL FANTASY XII THE ZODIAC AGE\<steam id>\keymap.ini`, in the game's own key
numbers. The exe holds the key names in reverse enum order (`Key_Apostrophe` ... `Key_Comma`); read backwards they give
A = 28 ... Z = 53, 1 ... 0 = 54 ... 63, Escape 64, F1 ... F12 65 ... 76, Enter 77, Backspace 78, Tab 79, Space 80,
`- = [ ] \ ; '` 81 ... 87, Comma 88, Period 89, Slash 90, Left Right Up Down 91 ... 94, Home End PgUp PgDn Insert Delete
95 ... 100, Numpad 1 ... 9, 0 101 ... 110, Numpad `/ * - + Enter .` 111 ... 116, Left Ctrl 117, Left Shift 118, Left Alt
119, Right Ctrl 120, Right Shift 121, Right Alt 122, Grave 123. Every binding in the file fits this (W = 50, the arrows
turn the camera, Escape = Start, R2 = 117 = Left Ctrl).

God mode (the bot's `memtool god`, and `trip` unless `FF12_BOT_GOD=0`) keeps every living character at full HP by
writing the character records ([game internals](game-internals.md)); it prints a `DEATH` line if someone falls anyway.
Watch for that line (and for the trip's end lines) with a monitor on every test run.

Don't teleport by writing the position (`memtool setpos`). It can put the party inside the floor or out of bounds.
Walk instead.

## The test bot

Build it with `dotnet build -c Release` in `tools/testbot`. Full command list: [tools/README.md](../tools/README.md).

- `memtool where`: map, story flag, position, camera.
- `memtool tnav <x> <z>`: walks to a point on the current map.
- `memtool trip <goalMapHex> [x z]`: walks across maps over the game's own exits. For example, `trip 81 182.7 294.8`
  goes to South Bank Village's crystal, and `trip 131 238 72` goes back to the Eastgate. Verified in game both ways
  (2026-11-14).

To watch a trip, or record it next to the game, run `memtool viz`: a live window with the map, the party, the
route being followed and the chain of maps.

**Run walks in the background and watch them.** Start `trip` / `tnav` as a background command, and put a monitor on
its output (that command's own output file, not "the newest file": a viewer started alongside it
writes one too) that matches every line you'd act on: `^\[` (each map), `heading`, `best:`, `blocked`, `stuck`, `no route`,
`did not lead`, `GAVE UP`, `map changed`, `arrived`, `TRIP`, `trip ended`, `timeout`, `Exception`, `DEATH`. Also
alert when the output stops changing for a few minutes: a trip can loop quietly (No. 11 Channel did for 20 minutes).
Monitors should be poll loops that end on their own; don't leave `tail -f` behind. Never block in the foreground
waiting for it. When it reports `blocked` or `stuck`, take a screenshot before deciding anything.

How it works, and what it has taught us:

- **Route over maps**: each map's exits come from its script's trigger lines and their mapjumps, read on demand by
  `tools/MapReader` ([game internals](game-internals.md#map-scripts-ebp)). The bot walks to a point just inside the
  line, then keeps walking outward until the map changes.
- **Route on a map**: A* over the map package's minimap picture, weighted to stay away from walls. It re-aligns to the
  live minimap every 3 seconds. The picture is the map package's, matched by the map's entrances; on a map with
  several minimap variants the bot uses the one that best matches the live minimap (`tnav` prints which).
- **Steering**: the route is smoothed (line-of-sight shortcuts, then rounded corners) and followed by holding W and
  turning the camera with the arrow keys, since movement follows the camera. The camera is only turned when the heading
  is more than 10 degrees off (and until it is within 3), half the error per tap, at most one tap per 150 ms; the turn
  rate is measured once per trip and refined after every tap (it changes with speed mode). The bot releases held keys
  on any exit; if a crash still leaves one down, tap that key once (`scripts/game.ps1 -Keep -Steps key:w,...`:
  without `-Keep` that script closes the game when it's done, which looks like a crash).
- **Stuck detection**: under 1 unit of movement in 5 seconds marks a wall just ahead and plans around it. The bot
  gives up after 4 tries. Each time it prints `why:` lines from the map data: off the walk mesh, a barrier crossing,
  floor that only exists in another minimap variant (and what switches it), a trigger line, or nothing (then look at a
  screenshot: probably a battle, a dialog or an NPC). These walls last for **one walk only**. Story barriers open later in the game, so never
  save them.
- **Space with nothing to talk to opens the battle menu** ("Attack / No Available Targets"), which holds the party
  still; C closes it, but not while the flee key (Ctrl) is held. Stuck walks now do this. Crystals and NPCs answer
  Space only from very close (about half a unit; a crystal from one unit away opened the battle menu), so walk up with
  `goto` or use `teleport touch` for crystals and `talk <x> <z>` for people.
- **Talking to people**: `mapdata npcs` / `floats` give where they stand, `memtool actors` where they are now. A clerk
  behind a desk is talked to from the desk's front: walk there with `tnav` first (the clerk's facing says which side),
  then `talk`. Conversations can run several pages, and a walk started while one is open goes nowhere: page through
  with `step.sh space` and look at each screenshot. `step.sh` with no key only takes a screenshot.
- **Exits the planner doesn't know**: airship flights and escorts run from talking to someone, so `trip` reports "no
  route" out of an aerodrome. Do those by hand (see [game internals](game-internals.md#map-scripts-ebp)).
- **Maps to keep trips out of**: `FF12_BOT_AVOID=1D1,346,352`. Freezing Gorge (1D1) moves the story to 1973 on
  arrival; the Garamsythe channels (346, 352) need sluice gates the bot doesn't work, and a route through them loops.
- **When a walk on the minimap picture fails** (a cliff the picture doesn't show), `trip` walks over the walk mesh
  instead (`meshnav`). It reported "arrived over the walk mesh" at Limatra Hills' way to Rava's Pass.
- **Stopping a sweep**: TaskStop on the background command leaves `sweep.sh` and its `memtool trip` running; the sweep
  is started with its pid saved (`bash tools/testbot/scripts/sweep.sh ... & echo $! > work/bot/sweep.pid`) so it can be killed. Two sweeps at once fight over the keys.
- **Boot crash**: a "Microsoft Visual C++ Runtime Library / Runtime Error!" at launch is the NVIDIA shader cache at
  256 MB (`trial.sh` deletes it; `game.ps1` doesn't).
- **Monsters attack while walking.** Use `exp_level=99` and `exp_weak=<text>`; `trip` also runs god mode (above), and
  holds the flee key.
- **Separate pieces of floor**: the walk mesh's wall edges (kind 0x08) split a map into pieces, even where two
  triangles touch (a cliff between them). The trip planner tracks which entrance it arrives at on each map and adds 15
  minutes to any exit on another piece than that entrance (`mapdata reach <map>|all` lists them). It isn't a hard block,
  because sluice gates and lifts change the floor in ways the mesh doesn't show. Maps whose script switches the minimap
  variant (Hourglass Basin's quicksand) are left to the live-minimap check. Within one piece, the minimap picture is
  still flat, so a route can still try to climb onto a plateau.
