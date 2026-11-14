# Testing in game

How to check a change in the running game, by hand or with the test bot in `tools/testbot`. The manual checklist
for each feature is in [test-checklist.md](test-checklist.md).

## Setup

- Run the game windowed at 1280x720, so screenshots and the minimap crop line up. The bot reads the minimap at
  window offset (1260, 30), 280x280.
- Back up the save folder before automating anything. A New Game autosaves into the first slot.
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

Don't teleport by writing the position (`memtool setpos`). It can put the party inside the floor or out of bounds.
Walk instead.

## The test bot

Build it with `dotnet build -c Release` in `tools/testbot`. Full command list: [tools/README.md](../tools/README.md).

- `memtool where`: map, story flag, position, camera.
- `memtool tnav <x> <z>`: walks to a point on the current map.
- `memtool trip <goalMapHex> [x z]`: walks across maps over the game's own exits. For example, `trip 81 182.7 294.8`
  goes to South Bank Village's crystal, and `trip 131 238 72` goes back to the Eastgate. Verified in game both ways
  (2026-11-14).

**Run walks in the background and watch them.** Start `trip` / `tnav` as a background command, and put a monitor on
its output that matches every line you'd act on: `^\[` (each map), `heading`, `best:`, `blocked`, `stuck`, `no route`,
`did not lead`, `map changed`, `arrived`, `TRIP`, `trip ended`, `timeout`, `Exception`. Never block in the foreground
waiting for it. When it reports `blocked` or `stuck`, take a screenshot before deciding anything.

How it works, and what it has taught us:

- **Route over maps**: `tools/mapdata/exits.csv`, read from the map scripts. Each map's exits come from its trigger
  lines and their mapjumps ([game internals](game-internals.md#map-scripts-ebp)). The bot walks to a point just
  inside the line, then keeps walking outward until the map changes. Regenerate the table with
  `dotnet run exits.cs -- csv exits.csv` in `tools/mapdata`.
- **Route on a map**: A* over the map package's minimap picture, weighted to stay away from walls. It re-aligns to the
  live minimap every 3 seconds. Pictures come from `scripts/extract-maps.sh`, and `mapfind` caches which package goes
  with which map in `work/mapres/tex/maps.csv`. If a walk goes into a wall from the start, check that cache first:
  the wrong package was once picked for `dst_a02`.
- **Stuck detection**: under 1 unit of movement in 5 seconds marks a wall just ahead and plans around it. The bot
  gives up after 4 tries. These walls last for **one walk only**. Story barriers open later in the game, so never
  save them.
- **Monsters attack while walking.** Use `exp_level=99` and `exp_weak=<text>`. There is no working god mode: the HP
  values in memory are display copies.
- **Plateaus**: the minimap picture is flat, so a route can go up a slope onto a plateau. The walk mesh in each
  package marks which floor areas connect, and using it for planning is the next improvement.
