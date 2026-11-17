# Developer tools

Tools for testing the randomizer in the running game. They are not part of the app. Game files are read from your
FFXII install (or the archive named by `FF12_VBF`) on demand and cached in `work/cache`; debug pictures go to
`work/bot`. `work/` is ignored by git.

## MapReader: what the game's files say about each map

`tools/MapReader` is a library that the test bot and `mapdata` use. `MapInfo.For(mapId)` reads a map the first time
it's asked for and gives:

- the map's script: entrances, exits (trigger lines and every map and entrance each leads to), other trigger lines,
  named points, and the actors that switch the map's minimap variant, with their story checks and whether the player
  has to use them;
- its map package, matched by finding the package whose walk mesh holds the map's entrances: the walk mesh, the world
  position of its pictures, and one minimap picture per variant;
- barriers: conditional walk-mesh crossings that aren't exits;
- variant changes: floor that exists in one minimap variant and not another, so where the game opens or closes a way;
- `Explain(point)`: why the party might be stuck there, from all of the above.

Nothing is copied into the repo as tables. The formats are described in the code and in
[docs/game-internals.md](../docs/game-internals.md).

## mapdata: look at a map's data

`dotnet run mapdata.cs -- <command>` in `tools/mapdata`:

- `map <map>`: package, entrances, exits, barriers, variant changes and what switches them. `<map>` is a script name
  (`gol_a02`) or a map id in hex (`1C0`).
- `route <from> <to>`: the fewest-maps route over the game's own exits.
- `explain <map> <x> <z>`: why the party might be stuck at a spot.
- `draw <map> <out.png> [variant]`: the minimap with the walk mesh, exits (cyan), barriers (red) and variant changes
  (yellow).
- `actor <map> <actor>`: one actor's script code. Call names come from ff12-script when it is checked out in
  `work/ff12-script`.
- `scan`: every map with barriers or variant changes. Slow the first time, since it reads every map.

## testbot: walk the game by itself

`tools/testbot` (`dotnet build -c Release`) reads the running game's memory (position, map, story flag, camera) and
presses keys. Main commands:

- `memtool where`: map id, story flag, party position and camera direction.
- `memtool tnav <x> <z>`: walks to a point on the current map. It plans the route on the map package's minimap
  picture, smooths it into curves (straight where there is clear line of sight, rounded corners), and steers along it
  by holding forward and turning the camera (`Helm.cs`: turns only when more than 10 degrees off, learns the camera's
  turn rate as it goes; `FF12_BOT_STEER=keys` for the older eight-direction key taps). On a map with several minimap variants it uses the one
  that best matches the live minimap. If the party stops moving for 5 seconds, it marks the spot ahead as a wall for
  this walk only and plans around it, and prints the map data's explanation for that spot (`why:` lines). It never
  saves these walls, because story barriers come and go.
- `memtool trip <goalMapHex> [x z]`: walks across maps. It takes the fewest-maps route over the game's own exits,
  walks to each exit's trigger line and crosses it. In the goal map it walks to (x, z).
  Example: `trip 81 182.7 294.8` goes from Rabanastre's Eastgate to South Bank Village's save crystal.
- `memtool viz`: a live window for recording next to the game (picture in picture). It shows the current map's
  picture, the party and its trail, the route the pathfinder is following right now (gold), every exit labelled with
  where it leads (the next one in gold), walls found on this walk, and the trip's chain of maps with the game's place
  names. It reads the position from the game itself, so it also follows you when you walk by hand. Start it before or
  during a `trip`; T toggles always-on-top. `tnav` and `trip` write what they are doing to `work/bot/live/*.json`.

Scripts in `testbot/scripts`:

- `step.sh [key/ms]`: presses one key or chord, then takes a screenshot (`work/bot/game/step.png`) and prints the
  position.
- `trial.sh <options>`: installs, launches the game and starts a New Game.
- `winshot.ps1 -Out <png>`: screenshot of the viewer window, to check how it looks.

Run long walks in the background, and watch their output for "blocked", "stuck", "why:" and "trip ended".

## dumpinfo

Reads a crash minidump: the exception, the loaded modules, the stack and the memory map.
