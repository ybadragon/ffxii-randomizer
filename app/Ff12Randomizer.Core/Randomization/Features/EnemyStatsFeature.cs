using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Enemy stats in every area's .ard file (default and per-level stat records, 0x38 bytes; layout from Bartz24's
/// FFModdingWPF, MIT): HP (u32 0x20), MP (u16 0x24) and Strength, Magick, Vitality, Speed, Evade, Defense, Magick Resist
/// and Attack (bytes 0x26-0x2D). Like Bartz24's randomizer, each enemy name gets one multiplier per stat, applied to its
/// default and level records; a record shared by several enemies is only scaled once. EXP, LP and gil don't change.
/// Areas are read when you enter them, so existing saves see the change.
/// </summary>
public sealed class EnemyStatsFeature : IRandomizerFeature
{
    public const string Id = "estats";
    private const int NameIdOffset = 0x08, DefaultIndexOffset = 0x22, LevelIndexOffset = 0x24;
    private const int HpOffset = 0x20, MpOffset = 0x24;
    private static readonly (string Name, int Offset)[] ByteStats =
    [
        ("strength", 0x26), ("magick", 0x27), ("vitality", 0x28), ("speed", 0x29),
        ("evade", 0x2A), ("defense", 0x2B), ("magickResist", 0x2C), ("attack", 0x2D),
    ];

    public bool IsEnabled(RandomizerSettings settings) => settings.ChoiceOf(OptionCatalog.AllGroups.First(g => g.Id == Id)) != "orig";

    public void Apply(RandomizerContext context)
    {
        bool sane = context.Settings.IsOn("sane");
        var rng = context.RngFor(Id);
        var pack = new BattlePack(context.Original.Extract(context.Original.Find(BattlePack.UsPath)!));
        var names = new MapNames(context.Original);
        var areas = context.Original.Entries.Where(e => AreaData.IsAreaFile(e.Name))
            .Select(e => new AreaData(e.Name, context.Edit(e.Name))).ToList();

        // Each enemy name's stat records, in a stable order; a record claimed by an earlier name isn't scaled again.
        var claimed = new HashSet<(string, bool, int)>();
        var byName = new SortedDictionary<ushort, List<(AreaData Area, bool Level, int Index)>>();
        foreach (var area in areas)
        {
            for (int r = 0; r < area.Basic.Count; r++)
            {
                var basic = area.Basic[r].Span;
                ushort nameId = BinaryPrimitives.ReadUInt16LittleEndian(basic[NameIdOffset..]);
                if (!byName.TryGetValue(nameId, out var list)) byName[nameId] = list = new();
                foreach (var (level, index) in new[] { (false, (int)basic[DefaultIndexOffset]), (true, (int)basic[LevelIndexOffset]) })
                {
                    var table = level ? area.LevelStats : area.DefaultStats;
                    if (index < table.Count && claimed.Add((area.ArchivePath, level, index))) list.Add((area, level, index));
                }
            }
        }

        foreach (var (nameId, records) in byName)
        {
            if (records.Count == 0) continue;
            double hpMp = Factor(rng, sane, sane ? (0.8, 1.25) : (0.5, 2.0));
            var factors = ByteStats.Select(_ => Factor(rng, sane, sane ? (0.85, 1.2) : (0.6, 1.6))).ToArray();
            object? spoiler = null;
            foreach (var (area, level, index) in records)
            {
                var record = (level ? area.LevelStats : area.DefaultStats)[index].Span;
                uint hpBefore = BinaryPrimitives.ReadUInt32LittleEndian(record[HpOffset..]);
                ushort mpBefore = BinaryPrimitives.ReadUInt16LittleEndian(record[MpOffset..]);
                var bytesBefore = new byte[ByteStats.Length];
                for (int s = 0; s < ByteStats.Length; s++) bytesBefore[s] = record[ByteStats[s].Offset];

                if (hpBefore > 0) BinaryPrimitives.WriteUInt32LittleEndian(record[HpOffset..], (uint)Math.Clamp(Math.Round(hpBefore * hpMp), 1, 99_999_999));
                if (mpBefore > 0) BinaryPrimitives.WriteUInt16LittleEndian(record[MpOffset..], (ushort)Math.Clamp(Math.Round(mpBefore * hpMp), 1, 9999));
                for (int s = 0; s < ByteStats.Length; s++)
                {
                    if (bytesBefore[s] > 0) record[ByteStats[s].Offset] = (byte)Math.Clamp(Math.Round(bytesBefore[s] * factors[s]), 1, 255);
                }

                if (!level && spoiler is null)
                {
                    var stats = new Dictionary<string, object>
                    {
                        ["hp"] = RandomizerContext.Change(hpBefore, BinaryPrimitives.ReadUInt32LittleEndian(record[HpOffset..])),
                        ["mp"] = RandomizerContext.Change(mpBefore, BinaryPrimitives.ReadUInt16LittleEndian(record[MpOffset..])),
                    };
                    for (int s = 0; s < ByteStats.Length; s++) stats[ByteStats[s].Name] = RandomizerContext.Change(bytesBefore[s], record[ByteStats[s].Offset]);
                    spoiler = new
                    {
                        enemy = pack.Text(BattlePack.NamesTable, nameId & 0x0FFF) ?? $"Enemy {nameId:X4}",
                        firstSeenIn = names.RegionOf(area.AreaId) ?? names.NameOf(area.AreaId + "01") ?? area.AreaId,
                        stats,
                    };
                }
            }
            if (spoiler is not null) context.Spoil("enemyStats", spoiler);
        }
    }

    private static double Factor(Rng rng, bool sane, (double Min, double Max) range) =>
        range.Min + rng.NextDouble() * (range.Max - range.Min);
}
