using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// What enemies drop, what Steal takes and what Poach gets, in every area's .ard file (basic info records, 0x58 bytes).
/// Slot layout, matched against published bestiary data (e.g. the Estersand Wolf: steals Wind Stone / Wolf Pelt / Pointy Hat,
/// poaches Wolf Pelt / Eye Drops):
///   0x28 0x2A 0x2C 0x2E  drops: common, uncommon, rare, very rare
///   0x32 0x34 0x36       steals: common, uncommon, rare
///   0x44 0x46            poaches: common, rare
///   0x4A 0x4E            bonus drops with the Monograph / Canopic Jar (the key items at 0x48 / 0x4C stay)
/// Gil rewards (0xE000 + amount) stay as they are. Item id 0x0000 is Potion but also fills unused slots, so a slot holding it is never changed and it is never written.
/// Every enemy of the same name gets the same result, so all Wolves agree. Areas are read when you enter them, so existing
/// saves see the change.
/// </summary>
public sealed class EnemyRewardsFeature : IRandomizerFeature
{
    public const string Id = "rewards", Drops = "drops", Steals = "steals", Poaches = "poaches";
    public const string Shuffle = "shuffle", Random = "random";

    private static readonly int[] DropSlots = [0x28, 0x2A, 0x2C, 0x2E, 0x4A, 0x4E];
    private static readonly int[] StealSlots = [0x32, 0x34, 0x36];
    private static readonly int[] PoachSlots = [0x44, 0x46];
    private const int NameIdOffset = 0x08;

    /// <summary>Slots can hold gil instead of an item: 0xE000 plus the amount (0xE064 = 100 gil). Those are left as they are.</summary>
    private const ushort GilFlag = 0xE000;

    public bool IsEnabled(RandomizerSettings settings) => Mode(settings) != "orig" && Kinds(settings).Count > 0;

    private static string Mode(RandomizerSettings settings) => settings.ChoiceOf(OptionCatalog.AllGroups.First(g => g.Id == Id));

    private static List<(string Kind, int[] Slots)> Kinds(RandomizerSettings settings)
    {
        var kinds = new List<(string, int[])>();
        if (settings.IsOn(Drops)) kinds.Add((Drops, DropSlots));
        if (settings.IsOn(Steals)) kinds.Add((Steals, StealSlots));
        if (settings.IsOn(Poaches)) kinds.Add((Poaches, PoachSlots));
        return kinds;
    }

    private sealed record Slot(AreaData Area, int Record, int Offset, ushort NameId, ushort Original)
    {
        public Span<byte> Bytes => Area.Basic[Record].Span.Slice(Offset, 2);
        public ushort Value { get => BinaryPrimitives.ReadUInt16LittleEndian(Bytes); set => BinaryPrimitives.WriteUInt16LittleEndian(Bytes, value); }
    }

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        string mode = Mode(context.Settings);
        var pool = ItemPools.Shuffleable(catalog);
        var areas = LoadAreas(context);

        foreach (var (kind, offsets) in Kinds(context.Settings))
        {
            var rng = context.RngFor($"{Id}.{kind}");
            var slots = new List<Slot>();
            foreach (var area in areas)
            {
                for (int r = 0; r < area.Basic.Count; r++)
                {
                    var record = area.Basic[r].Span;
                    ushort nameId = BinaryPrimitives.ReadUInt16LittleEndian(record[NameIdOffset..]);
                    foreach (int offset in offsets)
                    {
                        ushort item = BinaryPrimitives.ReadUInt16LittleEndian(record[offset..]);
                        if (item != 0 && catalog.Find(item) is { Shuffleable: true }) slots.Add(new Slot(area, r, offset, nameId, item));
                    }
                }
            }

            // One decision per enemy name and slot, applied to every record of that enemy.
            var decisions = slots.GroupBy(s => (s.NameId, s.Offset, s.Original)).ToList();
            var results = new Dictionary<(ushort, int, ushort), ushort>();
            if (mode == Shuffle)
            {
                foreach (var band in decisions.GroupBy(d => (d.Key.Offset, Group: ItemPools.SaneGroup(catalog.Find(d.Key.Original)!, sane))))
                {
                    var members = band.ToList();
                    var items = members.Select(m => m.Key.Original).ToList();
                    rng.Shuffle(items);
                    for (int i = 0; i < members.Count; i++) results[members[i].Key] = items[i];
                }
            }
            else
            {
                foreach (var decision in decisions)
                    results[decision.Key] = ItemPools.PickLike(rng, pool, catalog.Find(decision.Key.Original)!, sane, allowed: i => i.Id != 0).Id;
            }

            foreach (var slot in slots) slot.Value = results[(slot.NameId, slot.Offset, slot.Original)];
        }

        Spoil(context, areas);
    }

    private static List<AreaData> LoadAreas(RandomizerContext context) =>
        context.Original.Entries.Where(e => AreaData.IsAreaFile(e.Name))
            .Select(e => new AreaData(e.Name, context.Edit(e.Name)))
            .ToList();

    private static void Spoil(RandomizerContext context, List<AreaData> areas)
    {
        var catalog = ItemCatalog.Instance;
        var pack = new BattlePack(context.Original.Extract(context.Original.Find(BattlePack.UsPath)!));
        var names = new MapNames(context.Original);
        var originals = areas.ToDictionary(a => a.ArchivePath, a => context.Original.Extract(context.Original.Find(a.ArchivePath)!));
        string Item(ushort id) => id == 0xFFFF ? "-" : id >= GilFlag ? $"{id - GilFlag} gil" : catalog.NameOf(id);
        string[] Read(byte[] record, int[] offsets) => offsets.Select(o => Item(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(o)))).ToArray();

        var byEnemy = new Dictionary<string, (HashSet<string> Areas, object Entry)>();
        foreach (var area in areas)
        {
            var original = new AreaData(area.ArchivePath, originals[area.ArchivePath]);
            for (int r = 0; r < area.Basic.Count; r++)
            {
                var now = area.Basic[r].ToArray();
                var was = original.Basic[r].ToArray();
                if (now.AsSpan().SequenceEqual(was)) continue;
                ushort nameId = BinaryPrimitives.ReadUInt16LittleEndian(now.AsSpan(NameIdOffset));
                string enemy = pack.Text(BattlePack.NamesTable, nameId & 0x0FFF) ?? $"Enemy {nameId:X4}";
                string where = names.RegionOf(area.AreaId) ?? names.NameOf(area.AreaId + "01") ?? area.AreaId;
                string key = $"{enemy}|{string.Join(",", Read(now, [.. DropSlots, .. StealSlots, .. PoachSlots]))}";
                if (byEnemy.TryGetValue(key, out var seen)) { seen.Areas.Add(where); continue; }
                byEnemy[key] = ([where], new
                {
                    enemy,
                    drops = Read(now, DropSlots[..4]),
                    steals = Read(now, StealSlots),
                    poaches = Read(now, PoachSlots),
                    monograph = Item(BinaryPrimitives.ReadUInt16LittleEndian(now.AsSpan(0x4A))),
                    canopicJar = Item(BinaryPrimitives.ReadUInt16LittleEndian(now.AsSpan(0x4E))),
                    was = new
                    {
                        drops = Read(was, DropSlots[..4]),
                        steals = Read(was, StealSlots),
                        poaches = Read(was, PoachSlots),
                        monograph = Item(BinaryPrimitives.ReadUInt16LittleEndian(was.AsSpan(0x4A))),
                        canopicJar = Item(BinaryPrimitives.ReadUInt16LittleEndian(was.AsSpan(0x4E))),
                    },
                });
            }
        }

        foreach (var (key, value) in byEnemy.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var node = RandomizerContext.ToNode(value.Entry)!.AsObject();
            node.Insert(1, "areas", RandomizerContext.ToNode(value.Areas.Order(StringComparer.OrdinalIgnoreCase).ToArray()));
            context.Spoiler["enemies"] ??= new System.Text.Json.Nodes.JsonArray();
            context.Spoiler["enemies"]!.AsArray().Add(node);
        }
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        foreach (var area in LoadAreas(context))
        {
            for (int r = 0; r < area.Basic.Count; r++)
            {
                var record = area.Basic[r].ToArray();
                foreach (int offset in DropSlots.Concat(StealSlots).Concat(PoachSlots))
                {
                    ushort item = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset));
                    if (item != 0 && item != 0xFFFF && item < GilFlag && catalog.Find(item) is null)
                        yield return $"{area.AreaId} enemy {r} has an unknown item {item:X4} at 0x{offset:X2}.";
                }
            }
        }
    }
}
