# Developer tools

Tools for testing the randomizer in the running game. They are not part of the app and read the game's files through the
randomizer CLI, so build `app/Ff12Randomizer.Cli` (Debug) first. Extracted game files and debug pictures go to `work/`,
which git ignores.

## testbot: walk the game by itself

`tools/testbot` (`dotnet build -c Release`) reads the running game's memory (position, map, story flag, camera) and
presses keys. Main commands:

- `memtool where`: map id, story flag, party position and camera direction.
- `memtool tnav <x> <z>`: walks to a point on the current map. It plans the route on the map package's minimap
  picture, keeps away from walls, and steers along the route. If the party stops moving for 5 seconds, it marks the
  spot ahead as a wall for this walk only and plans around it. It never saves these walls, because story barriers
  come and go.
- `memtool trip <goalMapHex> [x z]`: walks across maps. It takes the fewest-maps route over the game's own exits
  (`mapdata/exits.csv`), walks to each exit's trigger line and crosses it. In the goal map it walks to (x, z).
  Example: `trip 81 182.7 294.8` goes from Rabanastre's Eastgate to South Bank Village's save crystal.
- `memtool viz`: a live window for recording next to the game (picture in picture). It shows the current map's
  picture, the party and its trail, the route the pathfinder is following right now (gold), every exit labelled with
  where it leads (the next one in gold), walls found on this walk, and the trip's chain of maps with the game's place
  names. It reads the position from the game itself, so it also follows you when you walk by hand. Start it before or
  during a `trip`; T toggles always-on-top. `tnav` and `trip` write what they are doing to `work/bot/live/*.json`.
- `memtool mapfind <texdir>`: finds which map package's picture belongs to the current map, and its exact position
  from the package's NPS2 record. It picks the package whose floor holds all of the map's entrances and exits.

Scripts in `testbot/scripts`:

- `step.sh [key/ms]`: presses one key or chord, then takes a screenshot (`work/bot/game/step.png`) and prints the
  position.
- `trial.sh <options>`: installs, launches the game and starts a New Game.
- `winshot.ps1 -Out <png>`: screenshot of the viewer window, to check how it looks.
- `extract-maps.sh`: rebuilds `work/mapres/tex`, the minimap picture of every map package.

Run long walks in the background, and watch their output for "blocked", "stuck" and "trip ended".

## mapdata: exits and walk meshes from the game files

- `exits.cs`: reads every map's exits from its script. Each exit has a trigger line, the map and entrance it leads
  to, and the map's entrance points. `dotnet run exits.cs -- csv exits.csv` regenerates the table; `route <from> <to>`
  prints a route; `names mapnames.csv` regenerates the game's place names that the viewer shows. The file format is
  in the comment at the top.
- `navdraw.cs`: draws a map package's walk mesh over its minimap picture. The mesh is the triangles of walkable floor
  that the game's AI routes over.
- `ebpdump.cs`: lists a map script's map jumps, or one actor's code.

## dumpinfo

Reads a crash minidump: the exception, the loaded modules, the stack and the memory map.
