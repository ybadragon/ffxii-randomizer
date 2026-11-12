# In-game test checklist

For each option: what to look at and where. The spoiler file (History in the app) has the expected values. "Existing
save" means load any save after relaunching the game.

| Option | Existing save? | What to check |
| --- | --- | --- |
| Shop prices | yes | Any shop: Potion is no longer 60 gil, and a magick shop's prices differ from vanilla (Cure is normally 200). Compare against `prices` in the spoiler. |
| Bazaar | yes | The bazaar in your current town: package contents match `bazaar` in the spoiler. |
| Enemy drops / steals / poaches | yes | Steal from a common enemy a few times. The item should be one listed for that enemy under `enemyRewards`. |
| Enemy stats | yes | Libra (or the bestiary) on a known enemy: HP or level differs from vanilla. |
| Elements | yes | Libra shows weaknesses. Compare with `elements` in the spoiler. |
| Weapon / armor stats | yes | Equipment menu: the attack of an owned weapon differs from vanilla and matches `gear`. |
| License boards / Espers / Quickenings | new game | After the prologue, the board layout matches `licenses`. |
| Starting equipment | new game | Party members join wearing the gear listed under `startingInventory` (empty slots are expected), and the gear listed under its items is in the inventory. Items only change with "Starting items too". |
| Party stats | new game | Status screen of the first party members. |
| Open world | new game | New game, play to the first free roam. Check that Rabanastre exits aren't story-locked and that a key item reward appears where `openWorldRewards` says. Note any crash with the map name. |
| Skip the prologue | new game | New Game goes straight to Vaan in the Garamsythe Waterway (no opening movie, no Reks). Vaan should be alone in the party with his usual starting gear, and the rat fight and the walk up to Rabanastre should play normally. |

Afterwards, put your own seed back: the app's History, or `reinstall 162895018` from the CLI.
