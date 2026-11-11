# Fixes to the open world base files

Bartz24's open world expects Insurgent's Manifesto, which extends what scripts may do. We run on the unmodded game, so
scripts that rely on the extension are repaired here. Found by booting the game with the open world installed: it aborted
("This application has requested the Runtime to terminate it in an unusual way") until `pre_z0000.ebp` was fixed.

## pre_z0000.ebp (`ps2data/plan_master/us/event/pre_z/pre_z0000/pre_z0000.ebp`)

`pre_z0000.c` is the decompiled source (ffgriever's ff12-script), `pre_z0000.ebp` the recompiled file (the original
0x80-byte EBP header followed by the compiled script).

Bartz24's version adds a block that keeps five story items equippable in step with their key items (Goddess's Magicite,
Manufacted Nethicite, Dawn Shard, Sword of Kings, Treaty-Blade). It counted equipped copies by reading
`import savemem u_short savemem_var_1[1368] = 0x04e494`, a 2.7 KB window of save memory the unmodded script VM rejects,
which aborts the game at boot. The rewrite keeps the same rule using only `haveitem` (inventory):

- key item held and the equippable copy missing: add one copy;
- key item gone and a copy present: unequip it and remove it.

Difference from the original: a copy that is equipped isn't counted, so an equipped copy can be joined by a second one in
the inventory. Everything else in the script is unchanged.

## naf_a01.ebp: no file change, a data change instead

The open world's new game setup (`randoInit` in `naf_a01.ebp`) reads each character's starting license board from price
record 0x77 (six base-13 digits, 0 = no preset board) and writes it through the same kind of save memory window. The
randomizer sets record 0x77 to 0, so no board is preset and that write never runs; boards are chosen in game as usual.
