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

`tools/MapReader` (`ScriptInfo`) reads all of this. The game has 723 exits across its maps.

Scripts also hold a `FF12POS3` table (positions with sizes, such as spawn areas) and `FF12AR03` (not decoded).

## Map packages (.mpk)

`ps2data/plan_master/map_ctrl/<area>/<package>/bin/<package>.mpk`, for example `dst01/dst0101/bin/dst0101.mpk`.
Package names don't reliably follow the map name, and several packages can cover the same spot (`dst_a02`'s spot is
covered by both `dst0102` and `dst0107`). The right package is the one whose walk mesh holds the map's entrances (`MapInfo` in `tools/MapReader` matches them this way).

- **Minimap pictures**: the TIM2 textures at least 512 wide, one per minimap variant (`setnavimapindex n` shows
  picture n). Red is walkable floor, the cyan glow is walls, black is outside. Where variants differ is where the game
  opens or closes a way (Golmore: variant 1 adds the passage at the Road of Verdant Praise exit). TIM2 here is PS2
  8-bit with a 256-colour RGBA palette (`Tim2` in `tools/MapReader`).
- **NPS2 record**: the 16-byte-aligned tag `NPS2`. At tag+16 are floats (x0, z0, width, height): the world point at
  the picture's top-left and the map's size. Picture pixel = (world - origin) x 4.
- **Walk mesh**: a block starting with u32 1, then u16 counts (vertices, triangles, edges, groups), u32 0x30, then
  section offsets relative to the block:
  - vertices: f32 x, z
  - triangles: u16 a, b, c, group
  - edges: u16 a, b, condition, kind. Condition 0x00FF on ordinary edges; otherwise the high byte is a condition
    number (0 to 31) and the crossing is conditional. Kind 0x2F joins two cells, 0x08 is a wall. Conditional
    crossings sit at exits, gates and barriers: Golmore's seal is condition 1. Which script call opens which condition
    number is not known yet.
  - group vertex lists
  - a 128 x 128 u16 table, probably next-hop routing between groups

  It lines up exactly with the minimap picture. It is the floor the game's AI routes over; a few triangles reach past the drawn floor.
  `mapdata.cs draw` draws it.

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

## Blockers that scripts switch on and off

Story barriers (seals, gates) are not in the minimap picture or the walk mesh. Map scripts switch them with these calls
(names from ff12-script):

| Call | Meaning |
| --- | --- |
| `0x00FE setmapidfloor(layer, id, on)` / `0x00FF setmapidwall(layer, id, on)` | Switch numbered floor / wall pieces on or off |
| `0x00F6`-`0x00F9` `resetmapidfloor[all]` / `resetmapidwall[all]` | Put them back |
| `0x021B setnavimapindex(n)` | Show minimap variant n (the package's n-th whole-map picture) |
| `showmapmodel` / `hidemapmodel`, `bgeffectplay` / `bgeffectstop` | The visible barrier (model, glow) |
| `fieldsign`, `reqenable` / `reqdisable`, `askpos` | Make an actor interactable: walk up, Space, answer a yes/no question |

Worked example, Golmore's glowing seal (`gol_a02`, around (175, 209)): actor `森人の道` checks story >= 1700 (open
world lowers that) and then makes the seal interactable. Answering yes runs actor `隠し通路`: floor pieces 0, 1, 2, 3
and 7 on, seal model 17 hidden, model 15 shown, glow effects 41 and 42 stopped, `setnavimapindex 1`. So the seal is a
door to use, not a missing road. The map's `FF12POS3` entry 7 is at the seal's spot.

The first argument of the floor and wall calls is not a layer: monster scripts call them too (over 170,000 calls),
probably to limit where each character may walk. Where the numbered pieces are stored is not decoded yet.

What the reader can already tell from the data, per map: barrier crossings, floor that changes between minimap
variants, and which actors switch variants (story checks, whether the player uses them, whether it depends on the
previous map). `mapdata.cs explain <map> <x> <z>` and the bot's `why:` lines use them.
