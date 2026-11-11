using System.Buffers.Binary;
﻿using Ff12Randomizer.Core.Vbf;

const string DefaultVbf = @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XII THE ZODIAC AGE\FFXII_TZA.vbf";

if (args.Length == 0)
{
    Console.WriteLine("usage: ff12rando list [filter] | extract <name> <outFile> | stats");
    return 1;
}

if (args[0] == "respoil")
{
    // Rewrites the spoiler log of the newest History run (or the one with seed args[1]) with the current labels, after
    // proving the current code rebuilds exactly the patch that run installed.
    var game = Ff12Randomizer.Core.Game.GameInstall.FindSteamInstall() ?? throw new InvalidOperationException("Game not found.");
    var paths = Ff12Randomizer.Core.Storage.AppPaths.Default;
    var history = new Ff12Randomizer.Core.Storage.HistoryStore(paths);
    var entry = history.Entries.FirstOrDefault(e => args.Length < 2 || e.Seed == args[1]) ?? throw new InvalidOperationException("No such run in History.");
    var archive = new VbfArchive(game.DataArchivePath);
    var run = Ff12Randomizer.Core.Randomization.RandomizerEngine.Run(archive, entry.Settings, new Ff12Randomizer.Core.Randomization.Seed(entry.Seed));
    string temp = Path.Combine(Path.GetTempPath(), $"ff12rando-respoil-{Guid.NewGuid():N}.f12p");
    try
    {
        VbfPatchBuilder.Build(archive, run.Files, temp);
        using var stream = File.OpenRead(temp);
        string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))[..16];
        if (fingerprint != entry.PatchedFingerprint)
            throw new InvalidOperationException($"This code makes a different game for seed {entry.Seed} ({fingerprint} vs {entry.PatchedFingerprint}); not rewriting its spoiler.");
    }
    finally { File.Delete(temp); }

    if (entry.SpoilerPath is { } old && File.Exists(old)) File.Copy(old, old + ".bak", overwrite: true);
    string written = Ff12Randomizer.Core.Storage.SpoilerLog.Write(paths, run, entry);
    Console.WriteLine($"seed {entry.Seed}: same game confirmed ({entry.PatchedFingerprint}); spoiler rewritten at {written}");
    return 0;
}

if (args[0] is "randomize" or "restore")
{
    // Full pipeline without the window: ff12rando randomize <seed> <toggle ids...> | restore
    var game = Ff12Randomizer.Core.Game.GameInstall.FindSteamInstall() ?? throw new InvalidOperationException("Game not found.");
    var paths = Ff12Randomizer.Core.Storage.AppPaths.Default;
    var service = new Ff12Randomizer.Core.RandomizerService(game, paths);
    if (args[0] == "restore")
    {
        Console.WriteLine(service.Patcher.Restore() ? "restored" : "nothing to restore");
        return 0;
    }
    var settings = Ff12Randomizer.Core.Randomization.RandomizerSettings.Defaults();
    foreach (string id in args.Skip(2))
    {
        if (id.Split('=') is [var key, var value]) settings.Choices[key] = value; else settings.Toggles[id] = true;
    }
    var outcome = service.Run(settings, new Ff12Randomizer.Core.Randomization.Seed(args[1]), null,
        new Progress<Ff12Randomizer.Core.RunStep>(s => Console.WriteLine($"  {s}")));
    Console.WriteLine($"seed {outcome.Entry.Seed}: {outcome.Entry.FileCount} file(s), fingerprint {outcome.Entry.PatchedFingerprint}");
    Console.WriteLine($"spoiler {outcome.Entry.SpoilerPath}");
    Console.WriteLine($"status {service.Patcher.GetStatus()}");
    return 0;
}

var vbf = new VbfArchive(Environment.GetEnvironmentVariable("FF12_VBF") ?? DefaultVbf);

switch (args[0])
{
    case "list":
        foreach (var e in vbf.Entries.Where(e => args.Length < 2 || e.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"{e.Index,6} {e.Size,12} {e.Offset,12:X} {e.Flags:X8} {e.Name}");
        }
        break;

    case "extract":
        var entry = vbf.Find(args[1]) ?? throw new FileNotFoundException(args[1]);
        var data = vbf.Extract(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        File.WriteAllBytes(args[2], data);
        Console.WriteLine($"{entry.Name}: {data.Length} bytes");
        break;

    case "blocks":
        var be = vbf.Find(args[1]) ?? throw new FileNotFoundException(args[1]);
        using (var fs = File.OpenRead(vbf.Path))
        {
            long pos = (long)be.Offset;
            for (long b = 0; b < be.BlockCount; b++)
            {
                int len = vbf.CompressedBlockLength(be.BlockStart + b);
                fs.Position = pos;
                var head = new byte[4];
                fs.ReadExactly(head);
                Console.WriteLine($"block {be.BlockStart + b}: len {len} at {pos:X} head {Convert.ToHexString(head)}");
                fs.Position = pos;
                var blk = new byte[len == 0 ? VbfArchive.BlockSize : len];
                fs.ReadExactly(blk);
                foreach (var (label, mk) in new (string, Func<Stream, Stream>)[] {
                    ("zlib", s => new System.IO.Compression.ZLibStream(s, System.IO.Compression.CompressionMode.Decompress)),
                    ("deflate", s => new System.IO.Compression.DeflateStream(s, System.IO.Compression.CompressionMode.Decompress)) })
                {
                    try
                    {
                        using var d = mk(new MemoryStream(blk));
                        var ms = new MemoryStream();
                        d.CopyTo(ms);
                        Console.WriteLine($"   {label}: {ms.Length} bytes");
                    }
                    catch (Exception ex) { Console.WriteLine($"   {label}: {ex.GetType().Name}"); }
                }
                pos += len == 0 ? VbfArchive.BlockSize : len;
            }
        }
        break;

    case "verify":
        {
            long tableBlocks = (vbf.Header.Length - vbf.BlockTableOffset) / 2;
            long maxBlock = vbf.Entries.Max(e => e.BlockStart + e.BlockCount);
            Console.WriteLine($"block table {tableBlocks}, highest block used {maxBlock}");
            int step = args.Length > 1 ? int.Parse(args[1]) : 997, ok = 0, bad = 0;
            foreach (var e in vbf.Entries.Where(e => e.Index % step == 0 || e.Name.EndsWith("battle_pack.bin")))
            {
                try { vbf.Extract(e); ok++; }
                catch (Exception ex) { bad++; Console.WriteLine($"FAIL {e.Name}: {ex.Message}"); }
            }
            Console.WriteLine($"extracted {ok}, failed {bad}");
            break;
        }

    case "testpatch":
        {
            // Proof of concept: invert the Square Enix boot logo and round-trip battle_pack.bin.
            string patchPath = args[1];
            const string logo = "gamedata/titleimage/in/sqex logo size_ffx.tga";
            const string battlePack = "ps2data/image/ff12/test_battle/us/binaryfile/battle_pack.bin";
            var logoData = vbf.Extract(vbf.Find(logo)!);
            for (int i = 18; i < logoData.Length; i++)
            {
                logoData[i] = (byte)~logoData[i];
            }

            // Modes: relocate (original compressed bytes), recompress (our compression), logo (visible change).
            string mode = args.Length > 2 ? args[2] : "logo";
            var replacements = new Dictionary<string, byte[]>();
            var relocate = new List<string>();
            switch (mode)
            {
                case "relocate": relocate.Add(battlePack); break;
                case "recompress": replacements[battlePack] = vbf.Extract(vbf.Find(battlePack)!); break;
                default:
                    replacements[logo] = logoData;
                    // A texture that is visibly on the title screen: invert all pixel data of the menu font
                    // (a ~2.5 KB PhyreEngine header, then 0x3DE000 bytes of pixels).
                    const string font = "gamedata/d3d11/artdata/font/us/font00.dds.phyre";
                    var fontData = vbf.Extract(vbf.Find(font)!);
                    for (int i = fontData.Length - 0x3DE000; i < fontData.Length; i++)
                    {
                        fontData[i] = (byte)~fontData[i];
                    }
                    replacements[font] = fontData;
                    replacements[battlePack] = vbf.Extract(vbf.Find(battlePack)!);
                    break;
            }
            var patch = VbfPatchBuilder.Build(vbf, replacements, patchPath, relocate);
            foreach (var name in relocate)
            {
                replacements[name] = vbf.Extract(vbf.Find(name)!);
            }
            Console.WriteLine($"patch: header {patch.RealHeaderLength} -> {patch.VirtualHeaderLength}, appended {patch.AppendedLength} bytes");

            // Read everything back through the same logic the loader uses.
            var virt = new VbfArchive(vbf.Path, () => new VirtualVbfStream(vbf.Path, patchPath));
            int checkedFiles = 0;
            foreach (var (name, expected) in replacements)
            {
                if (!virt.Extract(virt.Find(name)!).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"replaced file {name} did not read back");
                }
                checkedFiles++;
            }
            foreach (var e in vbf.Entries.Where(e => e.Index % 1999 == 0 && !replacements.ContainsKey(e.Name)))
            {
                if (!virt.Extract(virt.Entries[e.Index]).AsSpan().SequenceEqual(vbf.Extract(e)))
                {
                    throw new InvalidDataException($"untouched file {e.Name} changed");
                }
                checkedFiles++;
            }
            using (var vs = new VirtualVbfStream(vbf.Path, patchPath))
            {
                var vheader = new byte[patch.VirtualHeaderLength];
                vs.ReadExactly(vheader);
                var trailer = new byte[16];
                vs.Position = vs.Length - 16;
                vs.ReadExactly(trailer);
                if (!System.Security.Cryptography.MD5.HashData(vheader).AsSpan().SequenceEqual(trailer))
                {
                    throw new InvalidDataException("header MD5 trailer does not match");
                }
            }
            Console.WriteLine($"virtual archive OK, {checkedFiles} files compared, header MD5 trailer matches");
            break;
        }

    case "whatread":
        {
            // Maps "fread N at OFFSET" lines in loader.log back to archive files, in first-read order.
            var byOffset = vbf.Entries.Where(e => e.Size > 0).OrderBy(e => e.Offset).ToArray();
            var starts = byOffset.Select(e => e.Offset).ToArray();
            var seen = new HashSet<string>();
            var rx = new System.Text.RegularExpressions.Regex(@"fread (\d+) at ([0-9a-f]+)");
            foreach (var line in File.ReadLines(args[1]))
            {
                var m = rx.Match(line);
                if (!m.Success)
                {
                    continue;
                }
                ulong at = Convert.ToUInt64(m.Groups[2].Value, 16);
                int i = Array.BinarySearch(starts, at);
                if (i < 0)
                {
                    i = ~i - 1;
                }
                string name = i >= 0 ? byOffset[i].Name : "(header)";
                if (at < (ulong)vbf.Header.Length)
                {
                    name = "(header)";
                }
                else if (at >= (ulong)new FileInfo(vbf.Path).Length)
                {
                    name = "(patch region)";
                }
                if (seen.Add(name))
                {
                    var e = vbf.Find(name);
                    Console.WriteLine($"{seen.Count,4} {e?.Size,10} {name}");
                }
            }
            break;
        }

    case "treasures":
        {
            var items = Ff12Randomizer.Core.GameData.ItemCatalog.Instance;
            int maps = 0, chests = 0, unknown = 0, failed = 0;
            foreach (var map in Ff12Randomizer.Core.GameData.TreasureData.Maps)
            {
                var mapEntry = vbf.Find(map.ArchivePath);
                if (mapEntry is null) { Console.WriteLine($"MISSING {map.ArchivePath}"); failed++; continue; }
                Ff12Randomizer.Core.GameData.EbpTreasureTable table;
                try { table = new(vbf.Extract(mapEntry), map); }
                catch (InvalidDataException e) { Console.WriteLine(e.Message); failed++; continue; }
                maps++;
                for (int i = 0; i < table.Count; i++)
                {
                    var t = table[i];
                    chests++;
                    for (int s = 0; s < 4; s++) if (items.Find(t.GetItem(s)) is null) unknown++;
                    if (args.Length > 1 && map.MapId.StartsWith(args[1]))
                        Console.WriteLine($"{map.MapId}#{i,-2} gil {t.GilChance,3}% ({t.GilCommon}/{t.GilRare}) spawn {t.SpawnChance,3}% respawn {t.Respawn,3} | " +
                            $"{items.NameOf(t.Common1)}, {items.NameOf(t.Common2)} | rare {items.NameOf(t.Rare1)}, {items.NameOf(t.Rare2)}");
                }
            }
            Console.WriteLine($"{maps} maps, {chests} chests, {unknown} item slots not in the catalog, {failed} maps failed");
            var byCategory = Ff12Randomizer.Core.GameData.TreasureData.Maps
                .Where(m => vbf.Find(m.ArchivePath) is not null)
                .SelectMany(m => { var t = new Ff12Randomizer.Core.GameData.EbpTreasureTable(vbf.Extract(vbf.Find(m.ArchivePath)!), m); return Enumerable.Range(0, t.Count).Select(i => t[i]); })
                .Where(t => t.GilChance < 100)
                .SelectMany(t => Enumerable.Range(0, 4).Select(t.GetItem))
                .GroupBy(id => items.Find(id)?.Category.ToString() ?? "?");
            foreach (var g in byCategory) Console.WriteLine($"  {g.Key}: {g.Count()} slots, e.g. {string.Join(", ", g.Distinct().Take(4).Select(items.NameOf))}");
            break;
        }

    case "shops":
        {
            var items = Ff12Randomizer.Core.GameData.ItemCatalog.Instance;
            var pack = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            var table = new Ff12Randomizer.Core.GameData.ShopTable(pack);
            int unknown = 0;
            foreach (var shop in table.Shops)
            {
                Console.WriteLine($"[{shop.Index}] root {shop.RootId} (0x{shop.RootId:X}) {shop.Lists.Count} stages, first: {string.Join(", ", shop.Lists.SelectMany(l => l.Items).Take(4).Select(items.NameOf))}");
                foreach (var (list, n) in shop.Lists.Select((l, n) => (l, n)))
                {
                    unknown += list.Items.Count(i => i != 0xFFFF && items.Find(i) is null);
                    if (args.Length > 1 && args[1] == shop.Index.ToString())
                        Console.WriteLine($"   stage {n}: {string.Join(", ", list.Items.Select(items.NameOf))}");
                }
            }
            Console.WriteLine($"{table.Shops.Count} shops, {unknown} unknown item ids");
            break;
        }

    case "dryrun":
        {
            // Runs the features without installing anything: dryrun <seed> <toggle or id=choice...>
            var settings = Ff12Randomizer.Core.Randomization.RandomizerSettings.Defaults();
            foreach (string id in args.Skip(2))
            {
                if (id.Split('=') is [var key, var value]) settings.Choices[key] = value; else settings.Toggles[id] = true;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var run = Ff12Randomizer.Core.Randomization.RandomizerEngine.Run(vbf, settings, new Ff12Randomizer.Core.Randomization.Seed(args[1]));
            Console.WriteLine($"{sw.ElapsedMilliseconds} ms, {run.Files.Count} files changed, {run.Problems.Count} problems");
            foreach (var p in run.Problems.Take(10)) Console.WriteLine($"  PROBLEM {p}");
            foreach (var (section, node) in run.Spoiler)
            {
                var list = node!.AsArray();
                Console.WriteLine($"[{section}] {list.Count} entries");
                foreach (var item in list.Take(args.Contains("-v") ? 1000 : 3)) Console.WriteLine($"  {item!.ToJsonString()}");
            }
            break;
        }

    case "places":
        {
            var places = new Ff12Randomizer.Core.GameData.PlaceNames(vbf);
            Console.WriteLine($"{places.SubCount} sub-locations, {places.MainCount} main locations");
            for (int i = 0; i < places.MainCount; i++) Console.Write($"[{i}] {places.Main(i)}  ");
            Console.WriteLine();
            foreach (int id in new[] { 0x2FB, 0x127, 0x12C, 0x122, 0x314, 0x27B }) Console.WriteLine($"map 0x{id:X}: {places.Sub(id)}");
            break;
        }

    case "shopplaces":
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var where = Ff12Randomizer.Core.GameData.ShopLocations.Find(vbf);
            var table = new Ff12Randomizer.Core.GameData.ShopTable(new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!)));
            foreach (var shop in table.Shops)
                Console.WriteLine($"{shop.Index,2} where: {(where.TryGetValue(shop.Index, out var p) ? string.Join(" | ", p) : "NOT FOUND")}");
            Console.WriteLine($"{where.Count}/{table.Shops.Count} lists located in {sw.ElapsedMilliseconds} ms");
            break;
        }

    case "checkplaces":
        {
            // Compares treasures.csv subareas with the game's own place names for the same map.
            var places = new Ff12Randomizer.Core.GameData.PlaceNames(vbf);
            var numbers = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Ff12Randomizer.Core", "Data", "mapAreas.csv"))
                .Select(l => l.Split(',')).Where(p => p.Length >= 2 && int.TryParse(p[0], System.Globalization.NumberStyles.HexNumber, null, out _))
                .GroupBy(p => p[1]).ToDictionary(g => g.Key, g => int.Parse(g.First()[0], System.Globalization.NumberStyles.HexNumber));
            int same = 0, differ = 0, missing = 0;
            foreach (var g in Ff12Randomizer.Core.GameData.TreasureData.Groups.DistinctBy(g => g.MapId))
            {
                string? game = numbers.TryGetValue(g.MapId, out int n) ? places.Sub(n) : null;
                if (game is null) { missing++; Console.WriteLine($"  no game name: {g.MapId} csv={g.Area}: {g.Subarea}"); }
                else if (string.Equals(game, g.Subarea, StringComparison.OrdinalIgnoreCase)) same++;
                else { differ++; Console.WriteLine($"  DIFFER {g.MapId}: csv '{g.Subarea}' vs game '{game}'"); }
            }
            Console.WriteLine($"{same} same, {differ} differ, {missing} without a game name");
            break;
        }

    case "ards":
        {
            // ards <area prefix> [table]: dumps a table of each matching area file as u16 words, naming item ids.
            var items = Ff12Randomizer.Core.GameData.ItemCatalog.Instance;
            string table = args.Length > 2 ? args[2] : "stats";
            var files = vbf.Entries.Where(e => Ff12Randomizer.Core.GameData.AreaData.IsAreaFile(e.Name)).ToList();
            Console.WriteLine($"{files.Count} area files");
            foreach (var e in files.Where(e => Path.GetFileName(e.Name).StartsWith(args[1])))
            {
                var area = new Ff12Randomizer.Core.GameData.AreaData(e.Name, vbf.Extract(e));
                var t = table switch { "ext" => area.Extended, "basic" => area.Basic, "level" => area.LevelStats, _ => area.DefaultStats };
                Console.WriteLine($"== {e.Name}: {t.Count} x 0x{t.RecordSize:X}");
                for (int i = 0; i < Math.Min(t.Count, 12); i++)
                {
                    var r = t[i].ToArray();
                    var words = Enumerable.Range(0, t.RecordSize / 2).Select(w => BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(w * 2)))
                        .Select(v => items.Find(v) is { } it && v != 0 ? $"{v:X4}={it.Name}" : $"{v:X4}");
                    Console.WriteLine($"  [{i}] {string.Join(" ", words)}");
                }
            }
            break;
        }

    case "buildcheck":
        {
            // Full pipeline into a temp file, never the game: buildcheck <seed> <options...>
            var settings = Ff12Randomizer.Core.Randomization.RandomizerSettings.Defaults();
            foreach (string id in args.Skip(2))
            {
                if (id.Split('=') is [var key, var value]) settings.Choices[key] = value; else settings.Toggles[id] = true;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var run = Ff12Randomizer.Core.Randomization.RandomizerEngine.Run(vbf, settings, new Ff12Randomizer.Core.Randomization.Seed(args[1]));
            if (run.Problems.Count > 0) { Console.WriteLine($"PROBLEMS: {string.Join("; ", run.Problems.Take(5))}"); return 1; }
            string temp = Path.Combine(Path.GetTempPath(), $"ff12rando-check-{Guid.NewGuid():N}.f12p");
            try
            {
                var patch = VbfPatchBuilder.Build(vbf, run.Files, temp);
                Ff12Randomizer.Core.Patching.PatchManager.Verify(vbf, run.Files, temp);
                Console.WriteLine($"OK: {run.Files.Count} files, patch {new FileInfo(temp).Length / 1024} KB, header {patch.RealHeaderLength}->{patch.VirtualHeaderLength}, all files read back, {sw.ElapsedMilliseconds} ms");
            }
            finally { File.Delete(temp); }
            break;
        }

    case "enemyext":
        {
            // enemyext <name>...: each named enemy's extended info record (0x54 bytes) in hex, once per distinct record.
            var pack = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            var wanted = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>();
            foreach (var e in vbf.Entries.Where(e => Ff12Randomizer.Core.GameData.AreaData.IsAreaFile(e.Name)))
            {
                var area = new Ff12Randomizer.Core.GameData.AreaData(e.Name, vbf.Extract(e));
                for (int r = 0; r < area.Basic.Count; r++)
                {
                    var basic = area.Basic[r].ToArray();
                    string? name = pack.Text(Ff12Randomizer.Core.GameData.BattlePack.NamesTable, BinaryPrimitives.ReadUInt16LittleEndian(basic.AsSpan(8)) & 0xFFF);
                    if (name is null || !wanted.Contains(name) || basic[0] >= area.Extended.Count) continue;
                    string hex = Convert.ToHexString(area.Extended[basic[0]].ToArray());
                    if (seen.Add(name + hex)) Console.WriteLine($"{name,-16} {area.AreaId}: {string.Join(" ", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)))}");
                }
            }
            break;
        }

    case "boards":
        {
            // Per board: cell count and how many cells hold each special license type; plus the largest id used.
            var pack = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            var licenses = pack.Table(12);
            int maxId = 0;
            for (int n = 1; n <= 12; n++)
            {
                var b = vbf.Extract(vbf.Find(Ff12Randomizer.Core.Randomization.Features.LicenseBoardsFeature.BoardPath(n))!);
                var ids = Enumerable.Range(0, 576).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8 + i * 2))).Where(v => v != 0xFFFF).ToList();
                maxId = Math.Max(maxId, ids.Max());
                var types = ids.GroupBy(id => id < licenses.Count ? licenses.Record(id)[5] : -1).ToDictionary(g => g.Key, g => g.Count());
                var byType = string.Join(" ", types.Where(t => t.Key is >= 1 and <= 14).OrderByDescending(t => t.Value).Take(3).Select(t => $"type{t.Key}x{t.Value}"));
                Console.WriteLine($"board {n,2} top types [{byType}]: {ids.Count} cells, essentials {ids.Count(i => i == 31)}, quickenings {types.GetValueOrDefault(19)}, espers {types.GetValueOrDefault(20)}, second board {types.GetValueOrDefault(30)}; first names: {string.Join(", ", ids.Take(3).Select(i => pack.Text(3, i)))}");
            }
            Console.WriteLine($"{licenses.Count} license records, largest id on a board {maxId}; license 31 is '{pack.Text(3, 31)}'");
            break;
        }

    case "party":
        {
            var items = Ff12Randomizer.Core.GameData.ItemCatalog.Instance;
            var pack = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            var party = pack.Table(Ff12Randomizer.Core.GameData.BattlePack.PartySection);
            for (int i = 0; i < party.Count; i++)
            {
                var r = party.Record(i).ToArray();
                var slots = Enumerable.Range(0, 10).Select(s => (Id: BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x58 + s * 2)), Amount: r[0x34 + s]))
                    .Where(s => s.Id != 0xFFFF).Select(s => $"{(items.Find(s.Id)?.Name ?? s.Id.ToString("X4"))} x{s.Amount}");
                Console.WriteLine($"{i,2} name[i+1]={pack.Text(8, i + 1),-12} level {r[0x2E],2}: {string.Join(", ", slots)}");
            }
            break;
        }

    case "extcalls":
        {
            // extcalls <dir>: for each edited script (file name = archive path with '/' -> '_'), count CALL-family
            // instructions targeting 0x1100-0x11FF (the Manifesto's VM extension) and other call ids, vs the vanilla file.
            static Dictionary<ushort, int> Calls(byte[] b)
            {
                var d = new Dictionary<ushort, int>();
                for (int p = 0; p + 3 <= b.Length; p++)
                {
                    if (b[p] is 0x58 or 0x59 or 0x5D or 0x5E)
                    {
                        ushort id = (ushort)(b[p + 1] | b[p + 2] << 8);
                        d[id] = d.GetValueOrDefault(id) + 1;
                    }
                }
                return d;
            }
            int withExt = 0, files = 0;
            var newIds = new Dictionary<ushort, int>();
            foreach (var path in Directory.GetFiles(args[1], "*.ebp"))
            {
                var vanillaEntry = vbf.Entries.FirstOrDefault(e => e.Name.Replace('/', '_') == Path.GetFileName(path));
                if (vanillaEntry is null) continue;
                files++;
                var edited = Calls(File.ReadAllBytes(path));
                var vanilla = Calls(vbf.Extract(vanillaEntry));
                int ext = edited.Where(kv => kv.Key is >= 0x1100 and <= 0x11FF).Sum(kv => kv.Value)
                        - vanilla.Where(kv => kv.Key is >= 0x1100 and <= 0x11FF).Sum(kv => kv.Value);
                if (ext > 0) withExt++;
                foreach (var (id, n) in edited) if (n > vanilla.GetValueOrDefault(id) + 2 && id is >= 0x1100 and <= 0x11FF) newIds[id] = newIds.GetValueOrDefault(id) + 1;
            }
            Console.WriteLine($"{files} edited scripts compared; {withExt} have more 0x11xx calls than vanilla");
            var added = new Dictionary<ushort, int>();
            foreach (var path in Directory.GetFiles(args[1], "*.ebp"))
            {
                var ve = vbf.Entries.FirstOrDefault(e => e.Name.Replace('/', '_') == Path.GetFileName(path));
                if (ve is null) continue;
                var ed = Calls(File.ReadAllBytes(path)); var va = Calls(vbf.Extract(ve));
                foreach (var (id, c) in ed) { int extra = c - va.GetValueOrDefault(id); if (extra > 0) added[id] = added.GetValueOrDefault(id) + extra; }
            }
            foreach (var (id, c) in added.OrderByDescending(kv => kv.Value).Take(25)) Console.WriteLine($"  +{c,5} call 0x{id:X4}");
            foreach (var (id, n) in newIds.OrderByDescending(kv => kv.Value).Take(10)) Console.WriteLine($"  call 0x{id:X4}: more often in {n} files");
            break;
        }

    case "st2e":
        {
            // st2e <archive path> [filter]: every entry's id and text
            var text = new Ff12Randomizer.Core.GameData.St2eText(vbf.Extract(vbf.Find(args[1])!));
            foreach (var (id, s) in text.All)
            {
                if (args.Length < 3 || s.Contains(args[2], StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"{id,6} {s.ReplaceLineEndings(" ")}");
            }
            break;
        }

    case "callscan":
        {
            // callscan <hexCallId>...: every "PUSHII n; CALL/CALLPOPA id" in the US map scripts, i.e. bytes
            // 4F lo hi (58|5D) idlo idhi (opcodes from ffgriever's ff12-script, BSD-2).
            var ids = args.Skip(1).Select(a => Convert.ToUInt16(a, 16)).ToHashSet();
            foreach (var e in vbf.Entries.Where(e => e.Name.StartsWith("ps2data/plan_master/us/plan_map/") && e.Name.EndsWith(".ebp")))
            {
                var b = vbf.Extract(e);
                var found = new List<string>();
                for (int p = 0; p + 6 <= b.Length; p++)
                {
                    if (b[p] != 0x4F || (b[p + 3] != 0x58 && b[p + 3] != 0x5D)) continue;
                    ushort call = (ushort)(b[p + 4] | b[p + 5] << 8);
                    if (!ids.Contains(call)) continue;
                    string first = p >= 3 && b[p - 3] == 0x4F ? $"{(short)(b[p - 2] | b[p - 1] << 8)}, " : "?, ";
                    found.Add($"{call:X4}({first}{(short)(b[p + 1] | b[p + 2] << 8)})");
                }
                if (found.Count > 0) Console.WriteLine($"{Path.GetFileNameWithoutExtension(e.Name)}: {string.Join(" ", found)}");
            }
            break;
        }

    case "fields":
        foreach (var e in vbf.Entries.Where(e => e.BlockStart != e.Flags))
        {
            long compressed = 0;
            for (long b = 0; b < e.BlockCount; b++)
            {
                int l = vbf.CompressedBlockLength(e.BlockStart + b);
                compressed += l == 0 ? VbfArchive.BlockSize : l;
            }
            Console.WriteLine($"{e.Index,6} start {e.BlockStart,7} second {e.Flags,7} blocks {e.BlockCount,5} size {e.Size,10} comp {compressed,10} {e.Name}");
        }
        break;

    case "stats":
        Console.WriteLine($"files {vbf.Entries.Count}, header {vbf.Header.Length}");
        foreach (var g in vbf.Entries.GroupBy(e => e.Flags))
        {
            Console.WriteLine($"flags {g.Key:X8}: {g.Count()}");
        }
        foreach (var g in vbf.Entries.GroupBy(e => e.Name.Split('/')[0]))
        {
            Console.WriteLine($"root {g.Key}: {g.Count()}");
        }
        break;
}

return 0;
