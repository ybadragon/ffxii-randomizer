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
| Valley of the Dead (rwg_a01) | Reached on foot from South Bank Village by the crystal sweep (through the Ogir-Yensa and Nam-Yensa sandseas), crystal touched at story 420 | 2026-11-18 |
| Henne Mines (North Entrance) | By chocobo: hire one from Gurdy in Banks of the Sogoht (30 gil; the ride starts in Haulo Green), ride through Haulo Green's exit into The Shred's south part, dismount at the mine entrance ("The chocobo refuses to go any further") and walk in. The Imperial guards there don't stop the party; the Henne Mines banner shows and the party has control | 2026-11-18 |
| Shops | Gurdy's chocobo hire (30 gil, paid) and the Cartographers' Guild moogle (Ozmone Plain map 1700, Jahara 30) in Banks of the Sogoht | 2026-11-18 |
| Saving and loading | Saving at South Bank Village's crystal (to slot 006, the bot's own slot; slots 001-005 are left alone); the autosave in Banks of the Sogoht loads at story 420 with the level-99 party | 2026-11-18 |

## Story skip

| Where | What happens | Why (from the scripts) |
| --- | --- | --- |
| Paramina Rift, Freezing Gorge (pbc_a03) | Walking in from Head of the Silverflow at story 420 (reached through Golmore Jungle with Lente's Tear), at (264.4, 91.8) a scene starts, the story flag jumps from 420 to **1973** and the party is moved to the Archadian scene map `alc_z01` (whose director sets 2000). Seen in game by the crystal sweep, 2026-11-18. | `常駐ディレクター` in pbc_a03: `story >= 0 && story < 1973` -> `eventplay`, `story = 1973`, mapjump. In the main story nobody reaches Freezing Gorge before about 1970, so the check has no lower bound; with `openworld` any earlier visit skips everything in between. Not on the gate list. Fix candidates: a gate-list row that keeps Paramina Rift closed until its story point, or one that raises the lower bound of this check so the scene waits for the story. |

Other scripts that move the story forward the same way (check `story < N`, then set N) with no lower bound but 0, from a scan of every map script; **not seen in game**, and only a risk where open world lets the party in early: Hall of the Destroyer (rwg_b01, sets 1364; next to Valley of the Dead, which the sweep reached at story 420), Chamber of First Light (rwg_e01, 1390), Platform 1 - East Tanks (ene_a01, 1270), Augur Hill (enw_a03, 1338), Lhusu Square (byu_a04, 580), Ward of Measure (mrm_b01, 2057), Vault of the Champion (mrm_f01, 2200), Doubt Abandoned (rui_a04, 3120/3130), Tsenoble (alc_c01, 3260+), Trant (alc_b04, 3253), Nilbasse (alc_f01, 3255), the Pharos 66th/70th floors (dor_a01 3350, dor_c12 3440), the Giruvegan gates (gil_*, 4250-4370), Footfalls of the Past (rwf_a01, 5200), Womb of the Sun-cryst (dgl_f01, 5400). The full list (54 scripts): `dotnet run tools/mapdata/mapdata.cs -- skips`.

## Soft lock risk

| Where | What happens | Why (from the scripts) |
| --- | --- | --- |
| The Shred (south part) and the Henne Mines North Entrance, on foot | Reached by chocobo (above), the chocobo won't enter the mine, so the party gets off. On foot every way out of The Shred's south part is blocked: tall grass at the exits to Haulo Green and Field of Light Winds (walked into both, 2026-11-18); past the North Entrance the gate and the double door are shut (below), so the Henne Mines crystal can't be reached. Entering or leaving the mine autosaves there. | The Shred's wild chocobo (`野良騎乗チョコボ`, at (323.7, 289.6), seen wandering there) is the way out; its script checks `haveitem(0x20D8)` (Spoiled Gysahl Greens, loot) and opens the food window. Without greens the player can only reload an older save. Not seen in game: riding it out with greens (the bird wanders and the bot couldn't face it). Possible fixes: give Gysahl Greens with `openworld`, or open the Henne Mines doors (`ギミック扉`, story < 1830). |

## Blocked: worth a look

| Where | What blocks | Why (from the scripts) |
| --- | --- | --- |
| The Needlebrake, exit to Whisperleaf Way (Golmore Jungle) | The blue seal, until the party has Lente's Tear | `HED_DIREC` opens it only with key item **Lente's Tear** (0x80B7); `rewards.csv` places it in Eruyt Village. Confirmed: with the item given, the seal opens. |
| Eruyt Village, The Spiritwood: gate to vic_a04 | A shut gate (screenshot kept by the bot) | Static scenery: the exit lies off the walk mesh and no actor switches walls, floors or map-jump flags there, so it is only used by scenes. Lente's Tear comes from the Eruyt story event (around story 1740, `常駐監督`), so with `openworld` alone Golmore Jungle waits for that point in the story; the separate OpenWorld rewards (`rewards.csv`) place Lente's Tear in Eruyt after the Henne Mines. |
| The Shred, on foot | The Greensnake leads only to The Shred's north part, a dead end on foot; the exits from Haulo Green and Field of Light Winds are chocobo paths (off the walk mesh on foot, and Haulo Green's way out to Banks of the Sogoht takes the chocobo back) | The Shred's walk mesh is two pieces split by cliff walls (wall edges between touching triangles). Haulo Green and Field of Light Winds switch exit group 115 by `getchocobomodebit`. Not a block: the Henne Mines are reached by chocobo (above). |
| Henne Mines past the North Entrance | Both ways on are shut at story 420: the iron gate to Ore Separation ("It's locked from the other side": a shortcut opened from Ore Separation) and the double door to Pithead Junction A (no prompt) (2026-11-18) | The gate actor `ギミック扉` in hne_a01 keeps it shut while story < 1830 (minimap variant 2 or 3, exit group 347 released). `opengates.csv` has no row for hne_a01, so with `openworld` at story 420 the Henne Mines stop at the North Entrance. The story scenes around here are set at 1810 (The Shred, `イベント起動`) and 1820 (Staging Shaft), so the mines open with the story; a block that protects scenes. |

## Not tested: the bot can't route there yet

Most failed legs of the crystal sweep were the bot's own limits, not the game: airship flights (Rabanastre's aerodrome is reached and its Docent talks at story 420: "each counter serves a different destination"; the bot didn't find a ticket counter yet. The counter talk `乗船カウンタートーク` mapjumps to Bhujerba, Nalbina, Archades and Balfonheim), Barheim
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
