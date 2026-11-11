using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Enemy elemental affinities, in each area's .ard extended info records (0x54 bytes): element bitmasks at 0x29 absorb,
/// 0x2A immune, 0x2B half damage, 0x2C weakness and 0x2D boosted element, with bits Fire 1, Lightning 2, Ice 4, Earth 8,
/// Water 16, Wind 32, Holy 64, Dark 128 (bit order from Bartz24's FFModdingWPF, MIT; the fields were found from enemies
/// with well-known affinities: Fire Elemental absorbs Fire and is weak to Water, Zombies absorb Dark and are weak to Holy,
/// Flans are weak to Fire). Each enemy name gets its own reshuffle of the elements, applied to all five masks together,
/// so an enemy keeps the shape of its affinities (one absorbed element, one weakness...) but with different elements.
/// With Keep things sane, Holy and Dark stay as they are, so undead stay weak to Holy.
/// </summary>
public sealed class ElementsFeature : IRandomizerFeature
{
    public const string Id = "elements";
    private const int NameIdOffset = 0x08, ExtendedIndexOffset = 0x00, FirstMask = 0x29, MaskCount = 5;
    public static readonly string[] ElementNames = ["Fire", "Lightning", "Ice", "Earth", "Water", "Wind", "Holy", "Dark"];
    private static readonly string[] MaskNames = ["absorbs", "immune", "halves", "weakTo", "boosts"];

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    public void Apply(RandomizerContext context)
    {
        bool sane = context.Settings.IsOn("sane");
        var rng = context.RngFor(Id);
        var pack = new BattlePack(context.Original.Extract(context.Original.Find(BattlePack.UsPath)!));
        var areas = context.Original.Entries.Where(e => AreaData.IsAreaFile(e.Name))
            .Select(e => new AreaData(e.Name, context.Edit(e.Name))).ToList();

        // Extended records per enemy name; a record shared by several names is changed once, by the first.
        var claimed = new HashSet<(string, int)>();
        var byName = new SortedDictionary<ushort, List<(AreaData Area, int Index)>>();
        foreach (var area in areas)
        {
            for (int r = 0; r < area.Basic.Count; r++)
            {
                var basic = area.Basic[r].Span;
                ushort nameId = BinaryPrimitives.ReadUInt16LittleEndian(basic[NameIdOffset..]);
                int index = basic[ExtendedIndexOffset];
                if (!byName.TryGetValue(nameId, out var list)) byName[nameId] = list = new();
                if (index < area.Extended.Count && claimed.Add((area.ArchivePath, index))) list.Add((area, index));
            }
        }

        foreach (var (nameId, records) in byName)
        {
            if (records.Count == 0) continue;
            // A permutation of the element bits: sane keeps Holy (6) and Dark (7) in place.
            var moving = Enumerable.Range(0, sane ? 6 : 8).ToList();
            var target = moving.ToList();
            rng.Shuffle(target);
            var map = Enumerable.Range(0, 8).ToArray();
            for (int i = 0; i < moving.Count; i++) map[moving[i]] = target[i];

            object? spoiler = null;
            foreach (var (area, index) in records)
            {
                var record = area.Extended[index].Span;
                var before = record.Slice(FirstMask, MaskCount).ToArray();
                if (before.All(b => b == 0)) continue;
                for (int m = 0; m < MaskCount; m++) record[FirstMask + m] = Permute(before[m], map);
                if (spoiler is null && !record.Slice(FirstMask, MaskCount).SequenceEqual(before))
                {
                    var now = record.Slice(FirstMask, MaskCount).ToArray();
                    spoiler = new
                    {
                        enemy = pack.Text(BattlePack.NamesTable, nameId & 0x0FFF) ?? $"Enemy {nameId:X4}",
                        now = Describe(now),
                        was = Describe(before),
                    };
                }
            }
            if (spoiler is not null) context.Spoil("elements", spoiler);
        }
    }

    private static byte Permute(byte mask, int[] map)
    {
        int result = 0;
        for (int bit = 0; bit < 8; bit++) if ((mask & (1 << bit)) != 0) result |= 1 << map[bit];
        return (byte)result;
    }

    private static Dictionary<string, string[]> Describe(byte[] masks)
    {
        var result = new Dictionary<string, string[]>();
        for (int m = 0; m < MaskCount; m++)
        {
            var elements = Enumerable.Range(0, 8).Where(b => (masks[m] & (1 << b)) != 0).Select(b => ElementNames[b]).ToArray();
            if (elements.Length > 0) result[MaskNames[m]] = elements;
        }
        return result;
    }
}
