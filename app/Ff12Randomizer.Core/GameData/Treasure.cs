using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>A range of chests inside one map, from data/treasures.csv (Bartz24's FFModdingWPF, MIT).</summary>
public sealed record TreasureGroup(string Area, string Subarea, string MapId, int StartIndex, int Count, string Requirements, int Rank,
    IReadOnlySet<string> Traits)
{
    public bool Missable => Traits.Contains("Missable");
}

/// <summary>Where each map keeps its chest table, from data/treasureAddresses.csv: offset into the dynamic section and count.</summary>
public sealed record TreasureMap(string MapId, int Offset, int Count)
{
    /// <summary>The map's script in the archive, e.g. ps2data/plan_master/us/plan_map/ald_a/ald_a01/global/ald_a01.ebp.</summary>
    public string ArchivePath => $"ps2data/plan_master/us/plan_map/{MapId[..5]}/{MapId}/global/{MapId}.ebp";
}

public static class TreasureData
{
    public static IReadOnlyList<TreasureMap> Maps { get; } = Csv.ReadEmbedded("treasureAddresses.csv")
        .Where(r => r.Count >= 3 && int.TryParse(r[1], out _))
        .Select(r => new TreasureMap(r[0], int.Parse(r[1]), int.Parse(r[2])))
        .ToList();

    public static IReadOnlyList<TreasureGroup> Groups { get; } = Csv.ReadEmbedded("treasures.csv").Skip(1)
        .Where(r => r.Count >= 8 && int.TryParse(r[3], out _))
        .Select(r => new TreasureGroup(r[0], r[1], r[2], int.Parse(r[3]), int.Parse(r[4]), r[5], int.TryParse(r[6], out int rank) ? rank : 1,
            r[7].Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)))
        .ToList();

    /// <summary>The group a chest belongs to, if the data lists one.</summary>
    public static TreasureGroup? GroupOf(string mapId, int index) =>
        Groups.FirstOrDefault(g => g.MapId == mapId && index >= g.StartIndex && index < g.StartIndex + g.Count);
}

/// <summary>
/// The chest table inside a map's .ebp script: 0x18 byte records at section0 + dynamicOffset + mapOffset, where section0 is
/// the u32 at 0x10 and dynamicOffset the u32 at section0 + 0x40 (layout from Bartz24's FFModdingWPF, MIT).
/// </summary>
public sealed class EbpTreasureTable
{
    public const int RecordSize = 0x18;

    private readonly byte[] _data;
    private readonly int _start;

    public EbpTreasureTable(byte[] ebp, TreasureMap map)
    {
        _data = ebp;
        int section0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(0x10));
        int dynamicOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(section0 + 0x40));
        _start = section0 + dynamicOffset + map.Offset;
        Count = map.Count;
        if (_start < 0 || _start + Count * RecordSize > ebp.Length)
            throw new InvalidDataException($"{map.MapId}: chest table falls outside the file.");
    }

    public int Count { get; }

    public TreasureRecord this[int index] => new(_data.AsMemory(_start + index * RecordSize, RecordSize));
}

/// <summary>
/// One chest. A chest gives gil with <see cref="GilChance"/>% probability, otherwise an item: one of the two common items,
/// or with the Diamond Armlet equipped one of the two rare items.
/// </summary>
public sealed class TreasureRecord(Memory<byte> record)
{
    private Span<byte> S => record.Span;

    public byte Respawn => S[0x09];
    public byte SpawnChance => S[0x0A];
    public byte GilChance { get => S[0x0B]; set => S[0x0B] = value; }
    public ushort Common1 { get => Get(0x0C); set => Set(0x0C, value); }
    public ushort Common2 { get => Get(0x0E); set => Set(0x0E, value); }
    public ushort Rare1 { get => Get(0x10); set => Set(0x10, value); }
    public ushort Rare2 { get => Get(0x12); set => Set(0x12, value); }
    public ushort GilCommon { get => Get(0x14); set => Set(0x14, value); }
    public ushort GilRare { get => Get(0x16); set => Set(0x16, value); }

    /// <summary>The four item slots in order: common 1, common 2, rare 1, rare 2.</summary>
    public ushort GetItem(int slot) => Get(0x0C + slot * 2);
    public void SetItem(int slot, ushort id) => Set(0x0C + slot * 2, id);

    private ushort Get(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(S[offset..]);
    private void Set(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(S[offset..], value);
}
