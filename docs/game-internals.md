# Game internals

What we know about how FFXII TZA (Steam, x64) works inside, found while building and testing the randomizer.
Everything here was checked in game or against the files. Add to it when you find something new.

Archive and data table formats used by the features are in the [README](../README.md) ("Data formats used by the
features", "VBF format notes").

## Memory (FFXII_TZA.exe, offsets from the module base)

| Address | Type | What |
| --- | --- | --- |
| `exe+2044480` | u16 | Story flag (シナリオフラグ, savemem variable 0). Savemem variable N is at `exe+2044480+N`. |
| `exe+20441F0` | u16 | Current map id (`app/Ff12Randomizer.Core/Data/mapAreas.csv` maps id to script name) |
| `exe+21A7D20` | 3 x f32 | Position (x, y, z) of the character you control. Works in field maps and town-style maps. |
| `exe+2A2E750` | 3 x f32 | A party position that stops updating in town-style maps (seen in the Estersand Outpost). Don't use it. |
| `exe+1E5E528` | f32 | Camera: c = f32 at +0, s = f32 at +0x20. W moves the party along world (s, c), D along (-c, s). |
| `exe+1F7B088`, `exe+21A80A0` | | HP copies, 0xC0 bytes per member (current, then max). These are display copies: writing to them changes nothing. |

World axes: +x is east, -z is north, y is up. Neighbouring maps share world coordinates.

`tools/testbot` (`memtool where`, `posscan`, `findpair`, `find`, `get`) reads these and helps find new ones. `posscan
start` / `next` / `still` narrows down a moving value: walk between `next` calls, stand still between `still` calls.

## Map scripts (.ebp)

`ps2data/plan_master/us/plan_map/<area>/<map>/global/<map>.ebp`, for example `dst_a/dst_a01/global/dst_a01.ebp`.
The VM layout and opcodes come from ffgriever's ff12-script (`third_party/ff12-script`). `app/Ff12Randomizer.Core/GameData/MapScript.cs`
reads actors and instructions. Instructions are one byte, or three bytes (opcode, u16 operand) from opcode 0x48 up.

Useful opcodes and calls:

| Bytes | Meaning |
| --- | --- |
| `4F lo hi` | PUSHII constant |
| `48 v` / `49 v` | PUSHV / POPV variable (index into the script's variable table) |
| `5D lo hi` | CALLPOPA call id |
| call `0x008D` | mapjump(map, entrance, mode). Normal exits use mode 0. |
| call `0x011E` | Binds the actor to a trigger id. It is the first call in each `__MJ_CTRLnnn` actor. |
| call `0x048D` | Opens a shop (`openfullscreenmenu_48d(2, list)`) |
| calls `0x0353` / `0x0354` / `0x0355` | Add party member / remove party member / set battle members |

Map script data, at offsets relative to the script base (u32 at file offset 0x10):

- **Entrances**: `base + u32[base+0x54]` points to a u32 count, then 0x20-byte entries (x, y, z, facing, 16 bytes
  padding). Entrance n is where the party appears after a mapjump to this map with entrance n.
- **Exit trigger lines**: `base + u32[base+0x70]` points to a u32 list count, then u32 offsets (from base) to each
  list. A list is a u32 segment count, then from +0x10 segments of 0x20 bytes: (ax, ay, az, 1.0) and
  (bx, by, bz, flags). Bits 8-15 of flags are the trigger id. Several exits can share one list.
- **Linking a line to its destination**: the actor whose code starts with `PUSHII id; CALLPOPA 0x11E` owns trigger
  id. Its `PUSHII map; PUSHII entrance; PUSHII 0; CALLPOPA 0x8D` is where that exit leads. One exit can lead to
  different maps depending on the story.

`tools/mapdata/exits.cs` reads all of this. `tools/mapdata/exits.csv` holds every map's exits (723).

Scripts also hold a `FF12POS3` table (positions with sizes, such as spawn areas) and `FF12AR03` (not decoded).

## Map packages (.mpk)

`ps2data/plan_master/map_ctrl/<area>/<package>/bin/<package>.mpk`, for example `dst01/dst0101/bin/dst0101.mpk`.
Package names don't reliably follow the map name, and several packages can cover the same spot (`dst_a02`'s spot is
covered by both `dst0102` and `dst0107`). The right package is the one whose floor contains all of the map's entrances and exits (`memtool mapfind`).

- **Minimap picture**: the last TIM2 texture at least 512 wide. Red is walkable floor, the cyan glow is walls, black is
  outside. Story barriers (for example Golmore's glowing seal) are not in it. `tools/testbot/scripts/extract-maps.sh`
  extracts them all. TIM2 here is PS2 8-bit with a 256-colour RGBA palette (`memtool tim2` decodes it).
- **NPS2 record**: the 16-byte-aligned tag `NPS2`. At tag+16 are floats (x0, z0, width, height): the world point at
  the picture's top-left and the map's size. Picture pixel = (world - origin) x 4.
- **Walk mesh**: a block starting with u32 1, then u16 counts (vertices, triangles, edges, groups), u32 0x30, then
  section offsets relative to the block:
  - vertices: f32 x, z
  - triangles: u16 a, b, c, group
  - portal edges between groups: u16 a, b, 0xFF, attr
  - group vertex lists
  - a 128 x 128 u16 table, probably next-hop routing between groups

  It lines up exactly with the minimap picture. It is the floor the game's AI routes over. `tools/mapdata/navdraw.cs`
  draws it.

Other files: `ps2data/image/ff12/myoshiok/us/worldmap/menumap_subNN.bin` (NN = area id - 1, from `menumap_world.h`)
holds the coarse menu-map pictures and `LCMPOS1` points. Those points are **not** exits: walking to one led onto a
cliff.

## Crashes and hangs

- **Boot crash at `exe+B6E20`, even unmodified**: the NVIDIA shader cache
  `%LOCALAPPDATA%\NVIDIA\DXCache\fc52a91c35b3d6f9*` grew to 256 MB. It is mapped into low memory and pushes the game's
  32-bit buffers past 2 GB. Delete it; the game rebuilds it. `tools/testbot/scripts/trial.sh` does this.
- **Grown files crash or hang**: see the size rule in [AGENTS.md](../AGENTS.md).
- Each game crash writes a ~2 GB `.dmp` file in the game's `x64` folder. `tools/dumpinfo` reads it. Delete the dumps
  afterwards.
