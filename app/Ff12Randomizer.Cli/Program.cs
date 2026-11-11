using Ff12Randomizer.Core.Vbf;

const string DefaultVbf = @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XII THE ZODIAC AGE\FFXII_TZA.vbf";

if (args.Length == 0)
{
    Console.WriteLine("usage: ff12rando list [filter] | extract <name> <outFile> | stats");
    return 1;
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
                Console.WriteLine($"[{shop.Index}] {shop.Name} ({shop.Info?.Location}) {shop.Lists.Count} stages");
                foreach (var (list, n) in shop.Lists.Select((l, n) => (l, n)))
                {
                    unknown += list.Items.Count(i => i != 0xFFFF && items.Find(i) is null);
                    if (args.Length > 1 && shop.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase))
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
