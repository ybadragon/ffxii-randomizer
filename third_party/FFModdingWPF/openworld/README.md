# Open world base files

`openworld.zip` holds the game files Bartz24's FF12 Open World Randomizer ships as its starting point: edited map and event
scripts (`.ebp`) that open the world up, plus prepared `battle_pack.bin` sections (`battle_pack.bin.dir/section_NNN.bin`,
including section 61's text tables). Entries use the game archive's own paths.

- Source: https://github.com/Bartz24/FFModdingWPF, `FF12Rando/bin/data/ps2data/`
- Commit: `f20d432a035fc7b2cabb00fa90bfb114dec0e604` (version 0.8.2.65, 2026-11-07)
- License: MIT (see `../LICENSE`)
- Built with `ff12rando packopenworld`: files identical to the vanilla game are left out (168 of 979), so the pack only
  carries real changes (811 entries).

Every edited archive file fits within its original 64 KB block count, so the loader can serve all of them.
