# AGENTS.md

A guide for coding agents (and people) working on this repo. Keep this file short. Details belong in the files it
points to, and those files should be updated when you learn something new.

## What this is

A randomizer for FINAL FANTASY XII THE ZODIAC AGE (Steam, Windows). A WPF app builds a patch from the game's own data.
A small `dinput8.dll` loader that we ship serves the patch to the game, and the 31 GB archive is never written.
Background: [README.md](README.md) (features, how it works, archive format, layout).

## Rules

- **No third-party loader or mod framework.** Players install nothing but our exe. Ask before adding any package.
- **Derive mappings from game data, don't hardcode fixes.** If a lookup is wrong, find the real source of truth in the
  game files or script calls. If you can't, report what the data does and doesn't say. Don't add a one-off override
  table.
- **Patched files keep their exact size.** The game sizes buffers from `FileSizeTables\*.fst`, and a grown file
  crashes or hangs it. The archive header keeps its length, and replaced files reuse their own block slots (README,
  "VBF format notes").
- **A feature is "confirmed" only after it is seen in game.** Until then, mark it untested in the README table.
- **Prefer features that work on existing saves.**
- **Don't publish releases unless asked.**
- **The repo is public.** Keep machine paths, user names and personal data out of code, docs and commits.

## Layout

| Path | What |
| --- | --- |
| `app/Ff12Randomizer.Core` | Archive reader, patch builder, game data parsers (`GameData/`), features (`Randomization/Features/`), data tables (`Data/*.csv`) |
| `app/Ff12Randomizer.App` | WPF app |
| `app/Ff12Randomizer.Cli` | Developer commands: `list`, `extract`, `install <seed> <options>`, `dryrun`, `buildcheck`, script scans, ... (see `Program.cs`) |
| `loader/` | The `dinput8.dll` proxy (C++, `loader\build.cmd`) |
| `third_party/` | Reference code and data: Bartz24's FFModdingWPF (formats), ffgriever's ff12-script / ff12-text (script VM, text encoding) |
| `tools/` | Developer tools: the in-game test bot, map data readers, crash dump reader ([tools/README.md](tools/README.md)) |
| `docs/` | Notes: [game internals](docs/game-internals.md), [testing in game](docs/testing-in-game.md), [manual test checklist](docs/test-checklist.md), [ideas](docs/ideas.md) |
| `work/` | Git-ignored scratch space: extracted game files, debug pictures |

## Build and check

- `dotnet build app/Ff12Randomizer.slnx`: the app, Core and CLI.
- `Ff12Randomizer.Cli buildcheck`: builds and verifies a full patch in a temporary folder, without touching the game.
- `Ff12Randomizer.Cli install 1 <options...>`: installs a test seed into the game, without recording it in History.
  Option ids are the `Id` constants in `Randomization/Features/`. Test-only switches start with `exp_`
  ([testing in game](docs/testing-in-game.md)). Afterwards, reinstall the user's own seed (`reinstall <seed>`), or
  the plain options they asked for.
- `tools/testbot`: `dotnet build -c Release`, then [testing in game](docs/testing-in-game.md).

## Where to learn more

- [docs/game-internals.md](docs/game-internals.md): memory addresses, the map script format (exits, entrances, VM
  calls), map packages (minimap pictures, walk mesh), known crash causes.
- [docs/testing-in-game.md](docs/testing-in-game.md): how to drive the game for a test, the test bot, test switches,
  and the mistakes to avoid.
- [README.md](README.md): data formats used by the features, archive format, how spoiler names are found.
