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
    // Same code as the app's History "Update spoiler log" button.
    string written = new Ff12Randomizer.Core.RandomizerService(game, paths, history: history).RefreshSpoiler(entry);
    Console.WriteLine($"seed {entry.Seed}: same game confirmed ({entry.PatchedFingerprint}); spoiler rewritten at {written}");
    return 0;
}

if (args[0] == "owoverride")
{
    // owoverride <seed> <dir> [options...]: install a full open world run, with files from <dir> (archive paths with '/'
    // replaced by '_') swapped in on top. For testing script fixes.
    var game = Ff12Randomizer.Core.Game.GameInstall.FindSteamInstall()!;
    var patcher = new Ff12Randomizer.Core.Patching.PatchManager(game);
    var archive = patcher.OpenOriginal();
    var settings = Ff12Randomizer.Core.Randomization.RandomizerSettings.Defaults();
    settings.Toggles[Ff12Randomizer.Core.Randomization.Features.OpenWorldFeature.Id] = true;
    foreach (string id in args.Skip(3))
    {
        if (id.Split('=') is [var key, var value]) settings.Choices[key] = value; else settings.Toggles[id] = true;
    }
    var run = Ff12Randomizer.Core.Randomization.RandomizerEngine.Run(archive, settings, new Ff12Randomizer.Core.Randomization.Seed(args[1]));
    var files = run.Files.ToDictionary(kv => kv.Key, kv => kv.Value);
    foreach (var path in Directory.GetFiles(args[2]))
    {
        var entry = archive.Entries.FirstOrDefault(e => e.Name.Replace('/', '_') == Path.GetFileName(path)) ?? throw new FileNotFoundException(path);
        files[entry.Name] = File.ReadAllBytes(path);
        Console.WriteLine($"override {entry.Name} ({new FileInfo(path).Length} bytes, vanilla {entry.Size})");
    }
    string fp = patcher.Apply(archive, files, run.LoaderFeatures);
    Console.WriteLine($"installed {files.Count} files, fingerprint {fp}, loader features [{string.Join(", ", run.LoaderFeatures ?? [])}]");
    return 0;
}

if (args[0] == "owtest")
{
    // owtest <bp|bppad|pad>: install parts of the open world to find what the game rejects.
    //   bp     battle_pack with the open world sections, size changed
    //   bppad  the same, padded to the original size
    //   pad    everything, smaller files padded to their original size, larger files left out
    var game = Ff12Randomizer.Core.Game.GameInstall.FindSteamInstall()!;
    var patcher = new Ff12Randomizer.Core.Patching.PatchManager(game);
    var archive = patcher.OpenOriginal();
    var world = Ff12Randomizer.Core.GameData.OpenWorldBase.Load();
    var files = new Dictionary<string, byte[]>();
    byte[] Pad(byte[] data, int length) { var padded = new byte[length]; data.CopyTo(padded, 0); return padded; }
    var bp = archive.Extract(archive.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!);
    var pack = new Ff12Randomizer.Core.GameData.BattlePack(bp);
    var s61 = Ff12Randomizer.Core.GameData.BattlePack.Repack(pack.Section(61), new[] { 11, 12 }.ToDictionary(t => t, t => world.TextTables[t]), alignment: 4);
    var sections = new[] { 37, 28, 32 }.ToDictionary(s => s, s => world.Sections[s]);
    sections[61] = s61;
    var newBp = Ff12Randomizer.Core.GameData.BattlePack.Repack(bp, sections);
    Console.WriteLine($"battle_pack {bp.Length} -> {newBp.Length}");
    files[Ff12Randomizer.Core.GameData.BattlePack.UsPath] = args[1] == "bp" ? newBp : Pad(newBp, Math.Max(bp.Length, newBp.Length));
    if (args[1] == "pad")
    {
        int skipped = 0;
        foreach (var (path, data) in world.Files)
        {
            var e = archive.Find(path);
            if (e is null) continue;
            if (args.Length > 3 && path.Contains(args[3])) { skipped++; continue; }
            if (data.Length > (int)e.Size) { skipped++; if (args.Length < 3) continue; files[path] = data; continue; }
            files[path] = Pad(data, (int)e.Size);
        }
        Console.WriteLine($"{files.Count} files, {skipped} larger files left out");
    }
    string fp = patcher.Apply(archive, files, args[1] == "pad" ? ["hunt_rewards"] : []);
    Console.WriteLine($"installed {args[1]}: fingerprint {fp}");
    return 0;
}

if (args[0] is "install" or "reinstall")
{
    // install <seed> <options...>: apply a run to the game without recording it in History (for testing).
    // reinstall <seed>: re-apply a History run and check it produces the exact same patch it did originally.
    var game = Ff12Randomizer.Core.Game.GameInstall.FindSteamInstall() ?? throw new InvalidOperationException("Game not found.");
    var patcher = new Ff12Randomizer.Core.Patching.PatchManager(game);
    var archive = patcher.OpenOriginal();
    Ff12Randomizer.Core.Randomization.RandomizerSettings settings;
    string? expected = null;
    if (args[0] == "reinstall")
    {
        var entry = new Ff12Randomizer.Core.Storage.HistoryStore(Ff12Randomizer.Core.Storage.AppPaths.Default).Entries.First(e => e.Seed == args[1]);
        settings = entry.Settings;
        expected = entry.PatchedFingerprint;
    }
    else
    {
        settings = Ff12Randomizer.Core.Randomization.RandomizerSettings.Defaults();
        foreach (string id in args.Skip(2))
        {
            if (id.Split('=') is [var key, var value]) settings.Choices[key] = value; else settings.Toggles[id] = true;
        }
    }
    var run = Ff12Randomizer.Core.Randomization.RandomizerEngine.Run(archive, settings, new Ff12Randomizer.Core.Randomization.Seed(args[1]));
    if (run.Problems.Count > 0) throw new InvalidOperationException(run.Problems[0]);
    string fingerprint = patcher.Apply(archive, run.Files, run.LoaderFeatures);
    Console.WriteLine($"installed seed {args[1]}: {run.Files.Count} files, fingerprint {fingerprint}, loader features [{string.Join(", ", run.LoaderFeatures ?? [])}]"
        + (expected is null ? "" : fingerprint == expected ? " (matches History)" : $" (DIFFERENT from History {expected})"));
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

    case "extractmany":
        // extractmany <list file> <out dir>: extracts every archive path listed (one per line), keeping the paths.
        foreach (var name in File.ReadAllLines(args[1]).Where(l => l.Length > 0))
        {
            var target = Path.Combine(args[2], name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, vbf.Extract(vbf.Find(name) ?? throw new FileNotFoundException(name)));
        }
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
            // With a patch file as a third argument, offsets are resolved through the patched (virtual) archive.
            var view = args.Length > 2 ? new VbfArchive(vbf.Path, () => new VirtualVbfStream(vbf.Path, args[2])) : vbf;
            var byOffset = view.Entries.Where(e => e.Size > 0).OrderBy(e => e.Offset).ToArray();
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
                else if (args.Length < 3 && at >= (ulong)new FileInfo(vbf.Path).Length)
                {
                    name = "(patch region)";
                }
                if (seen.Add(name))
                {
                    var e = view.Find(name);
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

    case "spoilerview":
        {
            // What the app's spoiler viewer shows: spoilerview <spoiler.json>
            var doc = Ff12Randomizer.Core.Storage.SpoilerDocument.Load(args[1]);
            Console.WriteLine($"seed {doc.Seed}, {doc.CreatedAt}, {string.Join(" | ", doc.Summary)}");
            foreach (var t in doc.Tables)
            {
                Console.WriteLine($"[{t.Title}] {t.Rows.Count} rows: {string.Join(" | ", t.Columns)}");
                foreach (var row in t.Rows.Take(2)) Console.WriteLine("  " + string.Join(" | ", row));
            }
            return 0;
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
            if (Array.IndexOf(args, "-o") is var o and > 0 && o + 1 < args.Length)
            {
                // -o <dir>: also writes every changed file there, under its archive path.
                foreach (var (name, bytes) in run.Files)
                {
                    var target = Path.Combine(args[o + 1], name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, bytes);
                }
            }
            if (args.Contains("-t"))
            {
                var root = new System.Text.Json.Nodes.JsonObject();
                foreach (var (section, node) in run.Spoiler) root[section] = node?.DeepClone();
                foreach (var t in Ff12Randomizer.Core.Storage.SpoilerDocument.Parse(root).Tables)
                {
                    Console.WriteLine($"[{t.Title}] {t.Rows.Count} rows: {string.Join(" | ", t.Columns)}");
                    foreach (var row in t.Rows.Take(2)) Console.WriteLine("  " + string.Join(" | ", row));
                }
                break;
            }
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

    case "diffsize":
        {
            // diffsize <dir>: how different each edited file is from the vanilla archive copy (same length? changed bytes?)
            long total = 0, changedBytes = 0, sameLength = 0, files = 0, identical = 0;
            foreach (var path in Directory.GetFiles(args[1]).Where(p => !p.EndsWith(".txt")))
            {
                var ve = vbf.Entries.FirstOrDefault(e => e.Name.Replace('/', '_') == Path.GetFileName(path));
                if (ve is null) continue;
                var edited = File.ReadAllBytes(path);
                var vanilla = vbf.Extract(ve);
                files++;
                total += edited.Length;
                if (edited.AsSpan().SequenceEqual(vanilla)) { identical++; continue; }
                if (edited.Length == vanilla.Length) sameLength++;
                int n = Math.Min(edited.Length, vanilla.Length);
                for (int i = 0; i < n; i++) if (edited[i] != vanilla[i]) changedBytes++;
                changedBytes += Math.Abs(edited.Length - vanilla.Length);
            }
            Console.WriteLine($"{files} files ({total / 1048576.0:F1} MB): {identical} identical to vanilla, {sameLength} same length, {changedBytes / 1024} KB of differing bytes");
            break;
        }

    case "repacktest":
        {
            // Repacking battle_pack.bin (and its section 61) with no replacements must give back the identical file.
            var bp = vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!);
            var same = Ff12Randomizer.Core.GameData.BattlePack.Repack(bp, new Dictionary<int, byte[]>());
            var s61 = new Ff12Randomizer.Core.GameData.BattlePack(bp).Section(61);
            var same61 = Ff12Randomizer.Core.GameData.BattlePack.Repack(s61, new Dictionary<int, byte[]>(), alignment: 4);
            Console.WriteLine($"battle_pack identical: {same.AsSpan().SequenceEqual(bp)} ({same.Length} vs {bp.Length}); section 61 identical: {same61.AsSpan().SequenceEqual(s61)}");
            break;
        }

    case "packopenworld":
        {
            // packopenworld <dir of downloaded files, '/' replaced by '_'> <out.zip>: zips the files that differ from the
            // vanilla archive under their archive paths, plus pieces that aren't archive files (battle_pack sections).
            var known = File.ReadAllLines(Path.Combine(args[1], "..", "bartz_files.txt")).Select(l => l.Split(' ')[1]).ToList();
            int added = 0, skipped = 0;
            File.Delete(args[2]);
            using (var zip = System.IO.Compression.ZipFile.Open(args[2], System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (string archivePath in known)
                {
                    string local = Path.Combine(args[1], archivePath.Replace('/', '_'));
                    var bytes = File.ReadAllBytes(local);
                    var ve = vbf.Find(archivePath);
                    if (ve is not null && vbf.Extract(ve).AsSpan().SequenceEqual(bytes)) { skipped++; continue; }
                    var zipEntry = zip.CreateEntry(archivePath, System.IO.Compression.CompressionLevel.SmallestSize);
                    using var stream = zipEntry.Open();
                    stream.Write(bytes);
                    added++;
                }
            }
            Console.WriteLine($"{added} files packed, {skipped} identical to vanilla skipped, {new FileInfo(args[2]).Length / 1048576.0:F1} MB");
            break;
        }

    case "sectiondiff":
        {
            // sectiondiff <zip>: compares the zip's battle_pack.bin.dir pieces with the vanilla sections.
            var bp = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            var s61 = bp.Section(61);
            using var zip = System.IO.Compression.ZipFile.OpenRead(args[1]);
            foreach (var e in zip.Entries.Where(e => e.FullName.Contains("battle_pack.bin.dir/")).OrderBy(e => e.FullName))
            {
                using var ms = new MemoryStream(); using (var st = e.Open()) st.CopyTo(ms); var edited = ms.ToArray();
                var m = System.Text.RegularExpressions.Regex.Matches(e.FullName, @"section_(\d+)\.bin").Select(x => int.Parse(x.Groups[1].Value)).ToList();
                byte[] vanilla = m.Count == 2 ? new Ff12Randomizer.Core.GameData.BattlePack(s61).Section(m[1]) : bp.Section(m[0]);
                int diff = 0; for (int i = 0; i < Math.Min(edited.Length, vanilla.Length); i++) if (edited[i] != vanilla[i]) diff++;
                Console.WriteLine($"{string.Join("/", m),-6} vanilla {vanilla.Length,6} edited {edited.Length,6} differing bytes {diff}");
            }
            break;
        }

    case "ebpcalls":
        {
            // ebpcalls <archive path> <edited file>: call ids (PUSHII-preceded CALL family) used by vanilla vs edited.
            static List<(int At, ushort Id)> Calls(byte[] b)
            {
                var list = new List<(int, ushort)>();
                for (int p = 3; p + 3 <= b.Length; p++)
                    if (b[p] is 0x58 or 0x59 or 0x5D or 0x5E && (b[p - 3] == 0x4F || b[p - 1] is 0x00 or 0x01))
                        list.Add((p, (ushort)(b[p + 1] | b[p + 2] << 8)));
                return list;
            }
            var vanilla = vbf.Extract(vbf.Find(args[1])!);
            var edited = File.ReadAllBytes(args[2]);
            Console.WriteLine($"vanilla {vanilla.Length} bytes, edited {edited.Length} bytes");
            var v = Calls(vanilla).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Count());
            var e = Calls(edited).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var id in e.Keys.Union(v.Keys).OrderBy(i => i))
            {
                int a = v.GetValueOrDefault(id), b = e.GetValueOrDefault(id);
                if (a != b) Console.WriteLine($"  call 0x{id:X4}: vanilla {a}, edited {b}");
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

    case "text":
        {
            // text <index>: that entry of every battle_pack text table (section 61), to find which table names what.
            var bp = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            int index = Convert.ToInt32(args[1], 16);
            for (int t = 0; t < 64; t++)
            {
                string? s;
                try { s = bp.Text(t, index); } catch (Exception) { break; }
                if (s is not null) Console.WriteLine($"table {t,2} [{index:X}]: {s}");
            }
            break;
        }

    case "strings":
        {
            // strings <local file>: every NUL-separated run of game-encoded text with its byte offset.
            var bytes = File.ReadAllBytes(args[1]);
            int start = 0;
            for (int p = 0; p <= bytes.Length; p++)
            {
                if (p < bytes.Length && bytes[p] != 0) continue;
                if (p - start >= 3)
                {
                    string s = Ff12Randomizer.Core.GameData.GameText.Decode(bytes.AsSpan(start, p - start + (p < bytes.Length ? 1 : 0)));
                    if (s.Length >= 3) Console.WriteLine($"{start:X6} {s}");
                }
                start = p + 1;
            }
            break;
        }

    case "spawns":
        {
            // spawns <map ebp archive path>: actor name and unit of every btlAtelSetUnit call in a map script.
            foreach (var g in Ff12Randomizer.Core.Randomization.Features.EnemyLocationsFeature.Spawns(vbf.Extract(vbf.Find(args[1])!)).GroupBy(s => s))
                Console.WriteLine($"{g.Key.Actor} unit {g.Key.Unit} x{g.Count()}");
            break;
        }

    case "scriptvars":
        {
            // scriptvars <map script name>: the script's variable table (header 0x28): index, two u16s and a u32 each.
            string vp = vbf.Entries.First(e => e.Name.Contains("/us/") && e.Name.EndsWith("/" + args[1] + ".ebp")).Name;
            var eb = vbf.Extract(vbf.Find(vp)!);
            int sc = (int)BinaryPrimitives.ReadUInt32LittleEndian(eb.AsSpan(0x10));
            int vt = sc + (int)BinaryPrimitives.ReadUInt32LittleEndian(eb.AsSpan(sc + 0x28));
            int vn = (int)BinaryPrimitives.ReadUInt32LittleEndian(eb.AsSpan(vt));
            for (int i = 0; i < Math.Min(vn, 12); i++)
                Console.WriteLine($"{i}: {BinaryPrimitives.ReadUInt16LittleEndian(eb.AsSpan(vt + 4 + i * 8)):X4} {BinaryPrimitives.ReadUInt16LittleEndian(eb.AsSpan(vt + 6 + i * 8)):X4} {BinaryPrimitives.ReadUInt32LittleEndian(eb.AsSpan(vt + 8 + i * 8)):X8}");
            break;
        }

    case "storysteps":
        {
            // storysteps <from hex> <to hex>: every story flag assignment with a value in that range, by value, with its script and actor.
            int lo = Convert.ToInt32(args[1], 16), hi = Convert.ToInt32(args[2], 16);
            var found = new List<(short Value, string Where)>();
            foreach (var e in vbf.Entries.Where(e => e.Name.StartsWith("ps2data/plan_master/us/") && e.Name.EndsWith(".ebp") && !e.Name.Contains("/event/evt_t/")))
            {
                var ms = new Ff12Randomizer.Core.GameData.MapScript(vbf.Extract(e));
                if (ms.StoryFlag == 0xFFFF) continue;
                foreach (var w in ms.StoryWrites().Where(w => w.Value >= lo && w.Value <= hi))
                    found.Add((w.Value, $"{Path.GetFileNameWithoutExtension(e.Name)}:{w.Actor}"));
            }
            foreach (var g in found.GroupBy(f => f.Value).OrderBy(g => g.Key))
                Console.WriteLine($"0x{g.Key:X4} {g.Key}: {string.Join(", ", g.Select(f => f.Where).Distinct())}");
            break;
        }

    case "storychecks":
        {
            // storychecks <map script name>: every story flag comparison and assignment in that map's script, by actor.
            string mapPath = vbf.Entries.First(e => e.Name.Contains("/us/") && e.Name.EndsWith("/" + args[1] + ".ebp")).Name;
            var ms = new Ff12Randomizer.Core.GameData.MapScript(vbf.Extract(vbf.Find(mapPath)!));
            foreach (var g in ms.StoryChecks().GroupBy(c => (c.Actor, c.Compare, c.Value)))
                Console.WriteLine($"{g.Key.Actor}: flag {g.Key.Compare} {g.Key.Value} (0x{g.Key.Value:X}) x{g.Count()}");
            foreach (var w in ms.StoryWrites()) Console.WriteLine($"{w.Actor}: flag = {w.Value} (0x{w.Value:X})");
            break;
        }

    case "table":
        {
            // table <section>: every record of a battle_pack table as hex, one line each.
            var bp = new Ff12Randomizer.Core.GameData.BattlePack(vbf.Extract(vbf.Find(Ff12Randomizer.Core.GameData.BattlePack.UsPath)!));
            var t = bp.Table(int.Parse(args[1]));
            for (int i = 0; i < t.Count; i++) Console.WriteLine($"{i,4} {Convert.ToHexString(t.Record(i))}");
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
