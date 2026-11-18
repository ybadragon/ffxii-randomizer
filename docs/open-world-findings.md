# Open world: what the test bot found

Test setup: `install 1 exp_freeroam exp_flag=01A4 exp_map=81 exp_entry=0 exp_level=99 exp_weak=dst exp_give=2000:99 openworld`
(story flag 420, start at South Bank Village, level 99, 99 Teleport Stones, open world on). The bot walked with god
mode and the flee key on (see [testing in game](testing-in-game.md)). Dates are when it was seen.

## Works (seen in game)

| Where | What | Seen |
| --- | --- | --- |
| Rabanastre, Giza Plains, Ozmone Plain | Walking from South Bank Village through Giza and the Ozmone Plain to The Needlebrake | 2026-11-18 |
| The Needlebrake (gol_a02) | The hidden path (森人の道, Space at (271.0, 149.1)): "The path to Eruyt Village is gone. Speak to the Wood" opens it at story 420 | 2026-11-18 |
| Road of Verdant Praise (vic_a03), The Spiritwood (vic_a02) | Reached through the hidden path | 2026-11-18 |
| Teleport crystals | Touched and teleported between: Southgate, West Barbican, South Bank Village, Babbling Vale, Banks of the Sogoht, Yensa Border Tunnel, Road of Verdant Praise, The Parting Glade | 2026-11-17/18 |
| Golmore Jungle | With Lente's Tear (given by the bot), through the seal to Whisperleaf Way and The Parting Glade | 2026-11-18 |
| Zertinan Caverns | Hourglass Basin's boulder ("Space Boulder") changes the quicksand; the caverns lead out through Athroza Quicksands and The Balamka Fault to the Yensa side | 2026-11-18 |

## Blocked: worth a look

| Where | What blocks | Why (from the scripts) |
| --- | --- | --- |
| The Needlebrake, exit to Whisperleaf Way (Golmore Jungle) | The blue seal, until the party has Lente's Tear | `HED_DIREC` opens it only with key item **Lente's Tear** (0x80B7); `rewards.csv` places it in Eruyt Village. Confirmed: with the item given, the seal opens. |
| Eruyt Village, The Spiritwood: gate to vic_a04 | A shut gate (screenshot kept by the bot) | Static scenery: the exit lies off the walk mesh and no actor switches walls, floors or map-jump flags there, so it is only used by scenes. Lente's Tear comes from the Eruyt story event (around story 1740, `常駐監督`), so with `openworld` alone Golmore Jungle waits for that point in the story; the separate OpenWorld rewards (`rewards.csv`) place Lente's Tear in Eruyt after the Henne Mines. |
| Haulo Green and Field of Light Winds, exits to The Shred | Grass and rock where the exits are; the exits lie off the maps' walk meshes | No story check in either map's script. The Shred itself is reachable through The Greensnake. |
| The Shred, exit to North Entrance (Henne Mines) | Entering from The Greensnake, the live minimap shows the rest of The Shred cut off: no route to its exits to Haulo Green, Field of Light Winds or North Entrance (2026-11-18) | `ヘネ通行制限` (story < 1790) is on the gate list (rows 33-34). The map's own walk mesh (`mapdata draw 218`) is two separate pieces: a north piece with only The Greensnake exit, and a south piece with the North Entrance exit; the Haulo Green and Field of Light Winds exits lie off both. No walking route joins The Greensnake to the Henne Mines here; whatever links the pieces (a drop, a lift, a scene) isn't in the walk data. With the Haulo Green and Field of Light Winds passages also blocked, the Henne Mines may be unreachable at story 420. Next: look in game for a drop or prompt along the north piece's south edge. |

## Not tested: the bot can't route there yet

Most failed legs of the crystal sweep were the bot's own limits, not the game: airship flights (aerodrome menus), Barheim
Passage's inner links, Ridorana, Archades, the Feywood (from Golmore through gol_c01 and gol_d01). The exit reader finds 1185 of the game's 1776 map links
(`prefetchdata/ff12mapjump_custom_nocrystal.csv`).

## Leads

- `ps2data/image/ff12/us/bin/mapjumpgroupflagrom.bin` (FLAGROM1): 152 groups of story ranges (u16 lo, u16 hi | 0x8000;
  `..hi` from 0; FFFF ends). Exit groups switch on by story range here, outside the map scripts the open-world patch
  edits. How script flag ids (`setmapjumpgroupflag` 0x430 and up to 16426) map to these groups isn't decoded.
- Interaction spots (doors, switches, the hidden path) are in the script header's 0x74 table; the flee key hides their
  Space prompts.

## Key items for testing

`memtool keyitem <hex id> [off]` gives or takes a key item. Key items are bits: bit (id - 0x8000) from savemem
`exe+204B3C0`, with five more copies the game keeps in step (all are written). With Lente's Tear (80B7) given, The
Needlebrake's Golmore seal opened on the next map change and the party reached Whisperleaf Way (2026-11-18).
