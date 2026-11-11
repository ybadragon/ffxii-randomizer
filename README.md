# FFXII Randomizer

Randomizer for **FINAL FANTASY XII THE ZODIAC AGE** (Steam), in the spirit of
[ffx-randomizer](https://github.com/ybadragon/ffx-randomizer). It needs no third-party mod loader:
it ships its own small `dinput8.dll` loader, and the 31 GB game archive is never modified.

> Status: the loader is proven in game. Randomization features and the WPF app are next.

## How it works

The game keeps nearly all data in one archive, `FFXII_TZA.vbf`, which it reads through the C
runtime (`fopen` / `fread` / `_fseeki64`).

1. **The randomizer (C#)** extracts the tables it needs from the archive, randomizes them and writes
   `ff12rando\patch.f12p`: a rewritten archive header plus the recompressed replacement files.
2. **The loader (`x64\dinput8.dll`)** is loaded by the game automatically (it imports
   `DirectInput8Create`). It forwards that call to the real system DLL and patches the game's import
   table so reads of the archive are answered from a *virtual* archive: our header, the untouched
   real data, then our appended data.

Uninstalling means deleting `x64\dinput8.dll` and the `ff12rando` folder. With no `ff12rando`
folder the loader does nothing at all.

## VBF format notes (verified against the Steam build)

All little endian.

| Offset | Content |
| --- | --- |
| 0x00 | `SRYK` |
| 0x04 | u32 header length (first file's data starts here) |
| 0x08 | u64 file count N |
| 0x10 | N × 16 byte MD5 of file names |
| … | N × 32 byte entries: u32 blockStart, u32 blockStart (again), u64 size, u64 dataOffset, u64 nameOffset |
| … | u32 name table length (**including** these 4 bytes), then NUL-terminated names |
| … | u16 per 64 KiB block: compressed length; 0 = stored full block, = remaining length → stored short block |
| end of file | 16 byte MD5 of the whole header, checked by the game at startup |

Each block is an independent zlib stream.

Constraints the game enforces (found by testing in game):

- The header must keep its exact length, and block indices must stay within the original table.
  A replaced file therefore reuses its own block-table slots, so it may use at most as many
  64 KiB blocks as the original.
- The trailing MD5 must match the (rewritten) header.

`FileSizeTables\FileSizeTable_XX.fst` maps the old PS2 file IDs to sizes (205000 × u32, `FFFFFFFF`
for unused). They will matter once a file changes size.

## Layout

- `loader/`: the `dinput8.dll` proxy (C++). Build with `loader\build.cmd` (MSVC Build Tools).
- `app/Ff12Randomizer.Core`: archive reader (`VbfArchive`), patch builder (`VbfPatchBuilder`) and a
  C# mirror of the loader's read logic (`VirtualVbfStream`) for testing without the game.
- `app/Ff12Randomizer.Cli`: developer commands: `list`, `extract`, `verify`, `testpatch`,
  `whatread` (maps a verbose `loader.log` back to file names).

Creating `ff12rando\verbose.txt` in the game folder makes the loader log every archive read.

## Credits

Data-table layouts will lean on [Bartz24/FFModdingWPF](https://github.com/Bartz24/FFModdingWPF)
(MIT) as a reference.
