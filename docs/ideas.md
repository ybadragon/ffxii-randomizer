# Ideas

Candidate features, ranked roughly by value for effort. "Saves" says whether an existing save picks the change up after
a game relaunch, which matters most for playing with friends. "Data" says how much is already known:

- **known**: we already read or write this data.
- **likely**: the table is identified, but the field still needs checking against in-game values.
- **research**: the format is unknown.

## Close to done / finishing what exists

| Idea | Saves | Data | Notes |
| --- | --- | --- | --- |
| Confirm the untested options in game | – | known | Biggest remaining risk. A short checklist per option (what to look at, where) would make tonight's test fast. |
| Open world key item placement | new game | likely | Bartz24's logic places the key items with a reachability solver. Today we ship his default layout, so every open world seed has the same key items in the same places. Needs his requirement graph (areas, connections, key item rules) ported. One-time chests may need his treasure bank (max 255 respawn ids). |
| Seed hash shown in game | yes | known | Write a short code (four item names, say) into a menu string in text table 61, so friends can see at a glance that they're on the same seed. |

## New randomizers that work on existing saves

| Idea | Saves | Data | Notes |
| --- | --- | --- | --- |
| EXP / LP / gil multipliers | yes | likely | Per-enemy values in the `.ard` basic info we already edit. Quality of life, or a challenge preset. |
| Magick and technick power, MP cost, element | yes | likely | Section 14 (543 × 60 byte action records). Pairs with the Elements option. Keep sane: no 0 MP Holy. |
| Weapon properties | yes | likely | Section 13 also has element, on-hit status, combo/crit rate, range and charge time. Gear stats only touches attack and defense today. |
| Accessory and armor effects shuffle | yes | likely | Augment ids on equipment (Bartz24 edits section 58 for this). For example, Ribbon on a different accessory. |
| Hunt, story and sidequest rewards (vanilla game) | yes, unclaimed | known | Section 37 is the reward table the open world already uses. Shuffle rewards for players who aren't on open world. |
| Rare game and enemy levels | yes | likely | Level and HP scaling per region from `.ard` stats. A "chaos" preset is possible but needs care around boss scripts. |
| Gambits in shops | yes | known | Shops currently leave gambits alone. Shuffling them is easy, but the sane mode should keep early gambits early. |
| Music shuffle | yes | research | The loader can serve any archive file, so swapping BGM files is cheap if the audio container allows it. Fun for streams. |

## Needs a new game

| Idea | Data | Notes |
| --- | --- | --- |
| License costs (LP) | likely | Section 12 records next to the type byte we already read. |
| Starting gil, gambits and gambit slots | likely | Party records (section 16) and prices. |
| Esper and Quickening details | likely | Which Esper needs which board is done. Esper stats and abilities are not. |
| Guest characters | research | Guest stats and gear live in the same party table. |

## Robustness and sharing

- **Settings string / permalink** like the FFX randomizer, so a friend can paste one line to get the identical seed.
- **Race mode**: hide the spoiler until a button is pressed, with the hash still shown for comparison.
- **Game update detection**: if Steam patches the archive, refuse to run the old patch instead of crashing. The
  fingerprint already exists. Surface it in the app with a clear message.
- **Automated smoke test**: launch the game windowed, wait for the title screen, screenshot it and close. This would
  catch boot crashes like the open world one before a human tests.
- **Other mods present**: we refuse to overwrite a foreign `dinput8.dll`. A clear message in the app about what to
  do (and a chain-load option) would help friends who use other mods.

## Suggested next three

1. In-game checklist for tonight's test, then mark the confirmed options in the README and the app badges.
2. Seed hash in game: cheap, and helps directly with playing alongside friends.
3. Open world key item placement, once the base open world is confirmed playable. It's the largest piece left and
   only worth it on a stable base.
