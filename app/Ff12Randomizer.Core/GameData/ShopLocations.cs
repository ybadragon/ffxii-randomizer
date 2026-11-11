using Ff12Randomizer.Core.Vbf;

namespace Ff12Randomizer.Core.GameData;

/// <summary>A place a shop's stock list is sold from: the region and the map, both in the game's own names.</summary>
public sealed record ShopPlace(string MapId, string? Region, string? Place)
{
    public override string ToString() => (Region, Place) switch
    {
        (not null, not null) when Region != Place => $"{Region}: {Place}",
        (_, not null) => Place,
        (not null, null) => $"{Region} ({MapId})",
        _ => MapId,
    };
}

/// <summary>
/// Which maps open each shop stock list, read from the map scripts. A merchant opens a shop with the VM call
/// openfullscreenmenu_48d (0x048D) with menu type 2 and the stock list number, which compiles to
/// <c>PUSHII 2; PUSHII list; CALL/CALLPOPA 0x048D</c> = bytes <c>4F 02 00 4F lo hi (58|5D) 8D 04</c>
/// (opcodes and call ids from ffgriever's ff12-script, BSD-2). Every one of the 57 lists is opened this way somewhere.
/// Map names come from the game's planmapname.bin via the map numbers in data/mapAreas.csv (Bartz24's FFModdingWPF, MIT).
/// </summary>
public static class ShopLocations
{
    private const string MapScripts = "ps2data/plan_master/us/plan_map/";

    public static IReadOnlyDictionary<int, IReadOnlyList<ShopPlace>> Find(VbfArchive archive)
    {
        var names = new MapNames(archive);

        var result = new Dictionary<int, List<ShopPlace>>();
        foreach (var entry in archive.Entries.Where(e => e.Name.StartsWith(MapScripts) && e.Name.EndsWith(".ebp")))
        {
            string mapId = Path.GetFileNameWithoutExtension(entry.Name);
            var lists = ListsOpenedBy(archive.Extract(entry));
            if (lists.Count == 0) continue;

            var shopPlace = new ShopPlace(mapId, names.RegionOf(mapId), names.NameOf(mapId));
            foreach (int list in lists)
            {
                if (!result.TryGetValue(list, out var where)) result[list] = where = new();
                if (!where.Any(p => p.ToString() == shopPlace.ToString())) where.Add(shopPlace);
            }
        }
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ShopPlace>)kv.Value);
    }

    /// <summary>Stock lists a map script opens: <c>4F 02 00 4F lo hi (58|5D) 8D 04</c>.</summary>
    public static IReadOnlySet<int> ListsOpenedBy(ReadOnlySpan<byte> script)
    {
        var lists = new HashSet<int>();
        for (int p = 0; p + 9 <= script.Length; p++)
        {
            if (script[p] == 0x4F && script[p + 1] == 0x02 && script[p + 2] == 0x00 && script[p + 3] == 0x4F
                && (script[p + 6] == 0x58 || script[p + 6] == 0x5D) && script[p + 7] == 0x8D && script[p + 8] == 0x04)
                lists.Add(script[p + 4] | script[p + 5] << 8);
        }
        return lists;
    }

}
