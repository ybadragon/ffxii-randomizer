using System.Buffers.Binary;
using System.Text;
using Ff12Randomizer.Core.Vbf;

namespace Ff12Randomizer.Core.GameData;

/// <summary>Decodes the game's English text encoding (single-byte characters; tags and multi-byte glyphs are skipped).</summary>
public static class GameText
{
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder();
        foreach (byte b in bytes)
        {
            if (b == 0) break;
            if (GameTextCharsEn.Map.TryGetValue(b, out string? ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}

/// <summary>
/// The in-game name of each map script (ald_a02 → "Alley of Low Whispers"): the map's number from data/mapAreas.csv
/// (Bartz24's FFModdingWPF, MIT) looked up in the game's own <see cref="PlaceNames"/>.
/// </summary>
public sealed class MapNames(PlaceNames places)
{
    private static readonly Dictionary<string, int> Numbers = Csv.ReadEmbedded("mapAreas.csv")
        .Where(r => r.Count >= 2 && int.TryParse(r[0], System.Globalization.NumberStyles.HexNumber, null, out _))
        .GroupBy(r => r[1])
        .ToDictionary(g => g.Key, g => int.Parse(g.First()[0], System.Globalization.NumberStyles.HexNumber));

    public MapNames(VbfArchive archive) : this(new PlaceNames(archive)) { }

    public PlaceNames Places { get; } = places;

    public string? NameOf(string mapId) => Numbers.TryGetValue(mapId, out int number) ? Places.Sub(number) : null;

    private Dictionary<string, string>? _regions;

    /// <summary>
    /// The region of a map or area id by its 3-letter prefix ("ald_a02" → Old Archades): the area the treasure data gives
    /// that prefix's maps, kept only when it is one of the game's own region names.
    /// </summary>
    public string? RegionOf(string mapOrAreaId)
    {
        _regions ??= BuildRegions();
        return mapOrAreaId.Length >= 3 ? _regions.GetValueOrDefault(mapOrAreaId[..3]) : null;
    }

    private Dictionary<string, string> BuildRegions()
    {
        var gameRegions = Enumerable.Range(0, Places.MainCount).Select(Places.Main).OfType<string>().ToHashSet();
        return TreasureData.Groups
            .GroupBy(g => g.MapId[..3])
            .Select(g => (Prefix: g.Key, Area: g.GroupBy(x => x.Area).OrderByDescending(a => a.Count()).First().Key))
            .Where(x => gameRegions.Contains(x.Area))
            .ToDictionary(x => x.Prefix, x => x.Area);
    }

    /// <summary>
    /// The game's name for a map, keeping a description's extra detail when it extends that name
    /// ("No. 11 Channel (water is drained)" for one of two maps the game calls "No. 11 Channel").
    /// </summary>
    public string? NameOf(string mapId, string? description)
    {
        string? game = NameOf(mapId);
        if (game is null) return description;
        return description is not null && description.Length > game.Length
            && description.Contains(game, StringComparison.OrdinalIgnoreCase) ? description : game;
    }
}

/// <summary>
/// The game's place names, ps2data/image/ff12/us/bin/planmapname.bin ("PLMN"): u32 sub-location count at 0x4 and
/// main-location count at 0x8, then a u16 offset per entry from 0x10 (0 = none), sub-locations first. A sub-location is
/// one map ("Alley of Low Whispers", "Amal's Weaponry"), indexed by map number. Layout from ffgriever's ff12-text (BSD-2).
/// </summary>
public sealed class PlaceNames
{
    public const string ArchivePath = "ps2data/image/ff12/us/bin/planmapname.bin";

    private readonly byte[] _data;

    public PlaceNames(VbfArchive archive)
    {
        _data = archive.Extract(archive.Find(ArchivePath) ?? throw new FileNotFoundException(ArchivePath));
        if (_data.Length < 0x10 || Encoding.ASCII.GetString(_data, 0, 4) != "PLMN") throw new InvalidDataException("planmapname.bin isn't a PLMN file.");
        SubCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(4));
        MainCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(8));
    }

    public int SubCount { get; }
    public int MainCount { get; }

    /// <summary>The name of map number <paramref name="mapNumber"/>, or null when it has none.</summary>
    public string? Sub(int mapNumber) => mapNumber >= 0 && mapNumber < SubCount ? At(mapNumber) : null;

    /// <summary>A region name ("Old Archades", "Rabanastre"...).</summary>
    public string? Main(int index) => index >= 0 && index < MainCount ? At(SubCount + index) : null;

    private string? At(int entry)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(0x10 + entry * 2));
        if (offset == 0 || offset >= _data.Length) return null;
        string text = GameText.Decode(_data.AsSpan(offset));
        return text.Length == 0 ? null : text;
    }
}
