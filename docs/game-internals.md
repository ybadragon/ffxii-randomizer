# Game internals

What we know about how FFXII TZA (Steam, x64) works inside, found while building and testing the randomizer.
Everything here was checked in game or against the files. Add to it when you find something new.

Archive and data table formats used by the features are in the [README](../README.md) ("Data formats used by the
features", "VBF format notes").

## Memory (FFXII_TZA.exe, offsets from the module base)

| Address | Type | What |
| --- | --- | --- |
| `exe+2044480` | u16 | Story flag (シナリオフラグ, savemem variable 0). Savemem starts here: script `savemem` variable N is at `exe+2044480+N`. |
| `exe+20441F0` | u16 | Current map id (`app/Ff12Randomizer.Core/Data/mapAreas.csv` maps id to script name) |
| `exe+2044288` | u32 | Gil |
| `exe+21A7D20` | 3 x f32 | Position (x, y, z) of the character you control. Works in field maps and town-style maps. |
| `exe+1E5E528` | f32 | Camera: c = f32 at +0, s = f32 at +0x20. W moves the party along world (s, c), D along (-c, s). |
| `exe+203F22C` | u8 | Cursor of the open dialog menu (0 = first option); also mirrored at `exe+1F7C684`. Confirmed on the crystal menu and its destination list. |
| `exe+203F238` | u8 | 1 while a dialog menu is open (used by `memtool teleport`; `exe+203F1A2`, `203F1AC`, `203F1B0` behave the same). Also during yes/no questions (switches, the hidden path). |
| `exe+203F228` | u8 | Index of the open menu's last line (lines = value + 1). |
| `exe+2092938` | u32 | Character records, 0x1C8 apart (Vaan, Ashe, Fran, Balthier, Basch, ...): max HP here, **current HP at +0x24**. Writing the current HP changes it in game (checked); `exe+1F7B088` (+0xC4 per battle slot) and `exe+21A80A0` (+0xC0) are display copies refreshed from it (writing them changes nothing). |
| `exe+2045072` | 31 x u16 | Teleport destination lines, `g_menu_telepo_dest` (savemem 0x0BF2): the key item of each touched crystal, in touch order (see Teleporting). |
| `exe+204B3C0` | bits | Key items: bit (id - 0x8000). The game keeps up to five more copies; match a copy by its whole 32 bytes, not a few bits (a loose match wrote 34 wrong places once). After a reload only two copies are in use. `memtool keyitem`. |
| `exe+204AF90` | u16 per item | Loot counts by item id, with copies 0x430 apart before the key-item copies. Writing a count isn't enough to give loot: the inventory's order list (near `exe+2050836`) isn't decoded. |
| about `exe+2A2E4F0` on | 0x130 each | The field's actors (party, people, monsters): position (x, y, z) at a fixed offset. Entries move between map loads; `memtool actors` finds the array by the controlled character's position. One entry (`exe+2A2E750`) stops updating in town-style maps: use `exe+21A7D20` for the party. |

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
- **Doors and gates** (actor type 1: the low byte of the u32 at +4 in the actor's 0x30-byte table entry) show a
  Space prompt with `PUSHII k; CALLPOPA 0x176` (fieldsign) and own trigger line **0x14 + k**; their mapjump is where
  the door leads. Used with Space at the line, not by walking across it (Southgate: `gate_south` fieldsign 0 = line
  0x14 into the city, the downtown door fieldsign 1 = line 0x15).

- **Exits with no line of their own**: doors whose mapjump ends in 1 (Lowtown's `sml_door_*`, which have only a
  Space spot) and exits whose segments carry id 0 that no actor claims by number (Salikawood's Path of Hours). The
  reader finds them from the other side: the map the exit leads to has an exit back here, to entrance e, and this
  exit is the free segment (or, for a door, the Space spot) nearest entrance e.
- **Where NPCs stand**: the generic NPCs (`NPC01`...) share one routine and take their spot from the script's float
  constants (header 0x24: u32 count, then f32 values; `op50 k` pushes constant k) as x, z, facing, chosen by the NPC's
  number. Each NPC's block often starts with a story check (`story >= N` or it returns), so who stands where changes
  with the story. `mapdata npcs <map>` lists likely spots, `mapdata floats <map>` the raw constants. In memory, the
  field's actors are an array of 0x130-byte entries with the position at a fixed offset (`memtool actors`, `memtool
  talk <x> <z>`).
- **Exits that run from talking**: airship desks (`乗船カウンタートーク`, a mapjump with mode 1 after the ticket is
  paid) and escorts (Nalbina's aerodrome guard `マップ帝国君移植版` asks "Headed into town?" and mapjumps to the West
  Ward). They have no trigger line, so the exit reader and the bot's planner don't see them; `mapdata jumps <map>`
  lists every mapjump in a script by actor.
- **Airship desks**: in Rabanastre's aerodrome (rbn_b01) four clerks each stand only from a story point: Nalbina from
  350 at (30.13, 97.63), the others from 670, 4000 and 5150. An empty desk means that route isn't flying yet. A ticket
  is 200 gil; the private cabin flies straight to the other aerodrome (seen), the leisure craft's mapjump goes to the Sky Saloon
  (not tried).

`tools/MapReader` (`ScriptInfo`) reads all of this. Against the game's own link table
(`prefetchdata/ff12mapjump_custom_nocrystal.csv`, 1776 map-to-map links), it finds 1282 (2026-11-18). The rest are
mostly airship flights and escorts (above), shop interiors, puzzle doors and other story versions of a map.

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
    number (0 to 31) and the crossing is conditional. Kind 0x2F joins two cells, 0x08 is a wall, also between two triangles that touch (a cliff:
    The Shred's mesh is two pieces this way; `mapdata reach` lists exits on other pieces). Conditional
    crossings sit at exits, gates and barriers: Golmore's seal is condition 1. Which script call opens which condition
    number is not known yet.
  - group vertex lists
  - a 128 x 128 u16 table, probably next-hop routing between groups

  It lines up exactly with the minimap picture. It is the floor the game's AI routes over; a few triangles reach past the drawn floor.
  `mapdata.cs draw` draws it.

Other files: `ps2data/image/ff12/myoshiok/us/worldmap/menumap_subNN.bin` (NN = area id - 1, from `menumap_world.h`)
holds the coarse menu-map pictures and `LCMPOS1` points. Those points are **not** exits: walking to one led onto a
cliff.

## Teleporting

Crystal menu (checked 2026-11-17): walk right up to the crystal. Space only opens its menu from about half a unit
from the spot `memtool teleport` walks to; from one unit away it opened the battle menu instead. Right after arriving
on a map a crystal can ignore Space for a while (seen at Walk of Mind and The Western Divide; trying again 15 s later
worked). Space: Save / Teleport / Done (cursor on Save); S, Space: "Select a
destination", one line per touched crystal (see below), then Cancel; pick, Space. One
Teleport Stone per teleport (`exp_give=2000:99` gives 99). The crystal stands about 4 units from its
landing spot in direction (-sin f, -cos f), f = the landing entrance's facing (you land facing away from it).
`memtool teleport <map or key item>` does all of this from memory, no screenshots; `trip` uses it for teleport hops.

How the game does it (every crystal map's `system_crystal_actor` and `telepoeffectctrl`, decompiled with ff12-script
`-dc` on the script section of the .ebp, from the u32 at 0x10 to the u32 at 0x18):
- Each crystal has a hidden **key item**, 0x8040 and up (Southgate 0x8040, West Barbican 0x8042, South Bank Village
  0x8057, ...). Touching a crystal gives its item and appends it to `g_menu_telepo_dest[31]` (savemem 0x0BF2,
  `exe+2045072`) unless it's there already. Southgate's is added on every crystal's init, so a new game starts with it.
- The destination menu asks with 25 lines: line i shows the **name of item `g_menu_telepo_dest[i]`** (the region name,
  e.g. "Dalmasca Estersand" for South Bank Village), empty slots are hidden, then Cancel (result 25 or more). Slots fill
  in order, so the visible line number is the slot number.
- The chosen item goes to `telepoeffectctrl::telepoeffect`, which takes a Teleport Stone and switches on the item:
  `case 0x8057: mapjump(0x81, 0, 10)`. `mapdata.cs crystals` reads that switch (case values are PUSHI, an index into the
  script's integer table) and gives each crystal its key item, map and landing entrance.
- Choosing the crystal you stand at says you're already there.

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

Worked example, the hidden path to Eruyt Village in The Needlebrake (`gol_a02`, Space spot at (271.0, 149.1)): actor
`森人の道` checks story >= 1700 (open world lowers that) and then makes the glowing barrier interactable ("The path to
Eruyt Village is gone. Speak to the Wood"). Answering yes runs actor `隠し通路`: floor pieces 0, 1, 2, 3 and 7 on,
barrier model 17 hidden, model 15 shown, glow effects 41 and 42 stopped, `setnavimapindex 1`. So it is a door to use,
not a missing road (seen in game at story 420). The map's `FF12POS3` entry 7 is at its spot. The other seal on this
map, the way into Golmore Jungle (`HED_DIREC`), opens only with the key item Lente's Tear.

The first argument of the floor and wall calls is not a layer: monster scripts call them too (over 170,000 calls),
probably to limit where each character may walk. Where the numbered pieces are stored is not decoded yet.

What the reader can already tell from the data, per map: barrier crossings, floor that changes between minimap
variants, and which actors switch variants (story checks, whether the player uses them, whether it depends on the
previous map). `mapdata.cs explain <map> <x> <z>` and the bot's `why:` lines use them.
