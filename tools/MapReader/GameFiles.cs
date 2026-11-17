using Ff12Randomizer.Core.Game;
using Ff12Randomizer.Core.GameData;
using Ff12Randomizer.Core.Vbf;

namespace Ff12MapData;

/// <summary>
/// The game's archive, read on demand. Extracted files are cached under work/cache (git-ignored) so each is read from
/// the 31 GB archive once. Map ids, script names and place names come from the randomizer's map table and the game's
/// planmapname.bin.
/// </summary>
public static class GameFiles
{
    public static readonly string Repo = FindRepo();
    static readonly string Cache = Path.Combine(Repo, "work", "cache");
    /// <summary>work/cache: extracted game files, and (under derived/) answers worked out from them.</summary>
    public static string CacheDir => Cache;
    static readonly Lazy<VbfArchive> Archive = new(() =>
        new VbfArchive(Environment.GetEnvironmentVariable("FF12_VBF") is { Length: > 0 } v ? v
            : GameInstall.FindSteamInstall()?.DataArchivePath ?? throw new InvalidOperationException("FFXII isn't installed (or set FF12_VBF).")));
    static readonly Lazy<(Dictionary<int, string> byId, Dictionary<string, int> byName)> Table = new(LoadTable);
    static readonly Lazy<PlaceNames> Places = new(() => new PlaceNames(Archive.Value));

    /// <summary>A file from the archive (e.g. "ps2data/plan_master/us/plan_map/dst_a/dst_a01/global/dst_a01.ebp"), or null.</summary>
    public static byte[]? Get(string path)
    {
        var file = Path.Combine(Cache, path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file)) return File.ReadAllBytes(file);
        if (Archive.Value.Find(path) is not { } entry) return null;
        var data = Archive.Value.Extract(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, data);
        return data;
    }

    /// <summary>Every archive path that starts with <paramref name="prefix"/>.</summary>
    public static IEnumerable<string> List(string prefix) => Archive.Value.Entries.Select(e => e.Name).Where(n => n.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Script name ("dst_a01") of a map id, or null.</summary>
    public static string? ScriptName(int mapId) => Table.Value.byId.GetValueOrDefault(mapId);

    public static int? MapId(string scriptName) => Table.Value.byName.TryGetValue(scriptName, out int id) ? id : null;

    public static IEnumerable<int> AllMapIds => Table.Value.byId.Keys.Order();

    /// <summary>The game's own name for a map ("The Stepping"), falling back to the script name.</summary>
    public static string PlaceName(int mapId) => Places.Value.Sub(mapId) ?? ScriptName(mapId) ?? $"map 0x{mapId:X}";

    static (Dictionary<int, string>, Dictionary<string, int>) LoadTable()
    {
        var byId = new Dictionary<int, string>(); var byName = new Dictionary<string, int>();
        foreach (var line in File.ReadLines(Path.Combine(Repo, "app", "Ff12Randomizer.Core", "Data", "mapAreas.csv")).Skip(1))
        {
            var f = line.Split(',');
            if (f.Length < 2 || !int.TryParse(f[0], System.Globalization.NumberStyles.HexNumber, null, out int id)) continue;
            byId.TryAdd(id, f[1]); byName.TryAdd(f[1], id);
        }
        return (byId, byName);
    }

    static string FindRepo()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "app", "Ff12Randomizer.Core"))) return d.FullName;
        throw new InvalidOperationException("run from inside the randomizer repo");
    }
}
