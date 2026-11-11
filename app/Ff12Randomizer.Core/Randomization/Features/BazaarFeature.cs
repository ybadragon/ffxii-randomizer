using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// What the bazaar's packages give (battle_pack.bin section 57: 128 records of 0x24 bytes, layout from Bartz24's
/// FFModdingWPF, MIT). Each package gives up to three items (id/amount pairs at 0x04, 0x08, 0x0C; amount 0 = empty slot)
/// for a gil cost (0x10) and the loot ingredients that unlock it (0x16-0x20). Only what a package gives changes: the loot
/// needed to unlock it and its cost stay, and key items (Monographs, Canopic Jar...) never move. Package names come from
/// the game's text (section 61, table 9). The bazaar is read when you open it, so existing saves see the change.
/// </summary>
public sealed class BazaarFeature : IRandomizerFeature
{
    public const string Id = "bazaar";
    public const string Shuffle = "shuffle", Random = "random";
    public const int Section = 57, PackageNamesTable = 9;
    private static readonly int[] GiveSlots = [0x04, 0x08, 0x0C];
    private static readonly int[] IngredientSlots = [0x16, 0x1A, 0x1E];

    public bool IsEnabled(RandomizerSettings settings) => Mode(settings) != "orig";

    private static string Mode(RandomizerSettings settings) => settings.ChoiceOf(OptionCatalog.AllGroups.First(g => g.Id == Id));

    private sealed record Slot(Memory<byte> Record, int Offset, ItemInfo Original, ushort Amount)
    {
        public void Set(ushort item, ushort amount)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(Record.Span[Offset..], item);
            BinaryPrimitives.WriteUInt16LittleEndian(Record.Span[(Offset + 2)..], amount);
        }
    }

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        var rng = context.RngFor(Id);
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var table = pack.Table(Section);
        var before = Enumerable.Range(0, table.Count).Select(i => table.Record(i).ToArray()).ToList();

        var slots = new List<Slot>();
        for (int p = 0; p < table.Count; p++)
        {
            var record = pack.Data.AsMemory(RecordStart(pack, p), table.RecordSize);
            foreach (int offset in GiveSlots)
            {
                ushort item = BinaryPrimitives.ReadUInt16LittleEndian(record.Span[offset..]);
                ushort amount = BinaryPrimitives.ReadUInt16LittleEndian(record.Span[(offset + 2)..]);
                if (amount > 0 && catalog.Find(item) is { Shuffleable: true } info) slots.Add(new Slot(record, offset, info, amount));
            }
        }

        if (Mode(context.Settings) == Shuffle)
        {
            // Items move with their amounts (2 Phoenix Downs stay 2), within rank bands when sane.
            foreach (var band in slots.GroupBy(s => ItemPools.SaneGroup(s.Original, sane)))
            {
                var members = band.ToList();
                var contents = members.Select(s => (s.Original.Id, s.Amount)).ToList();
                rng.Shuffle(contents);
                for (int i = 0; i < members.Count; i++) members[i].Set(contents[i].Id, contents[i].Amount);
            }
        }
        else
        {
            var pool = ItemPools.Shuffleable(catalog);
            foreach (var slot in slots)
            {
                var pick = ItemPools.PickLike(rng, pool, slot.Original, sane);
                bool gear = pick.Category is ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Accessory;
                slot.Set(pick.Id, gear ? (ushort)1 : slot.Amount);
            }
        }

        Spoil(context, pack, before);
    }

    private static int RecordStart(BattlePack pack, int index)
    {
        int section = pack.SectionOffset(Section);
        int first = BinaryPrimitives.ReadUInt16LittleEndian(pack.Data.AsSpan(section + 0xC));
        int size = BinaryPrimitives.ReadUInt16LittleEndian(pack.Data.AsSpan(section + 0x8));
        return section + first + index * size;
    }

    private static void Spoil(RandomizerContext context, BattlePack pack, List<byte[]> before)
    {
        var catalog = ItemCatalog.Instance;
        var table = pack.Table(Section);
        string[] Items(ReadOnlySpan<byte> record, int[] offsets)
        {
            var list = new List<string>();
            foreach (int o in offsets)
            {
                ushort amount = BinaryPrimitives.ReadUInt16LittleEndian(record[(o + 2)..]);
                if (amount > 0) list.Add($"{catalog.NameOf(BinaryPrimitives.ReadUInt16LittleEndian(record[o..]))} x{amount}");
            }
            return list.ToArray();
        }

        for (int p = 0; p < table.Count; p++)
        {
            var now = table.Record(p);
            if (now.SequenceEqual(before[p])) continue;
            context.Spoil("bazaar", new
            {
                package = pack.Text(PackageNamesTable, p) ?? $"Package {p}",
                cost = BinaryPrimitives.ReadUInt32LittleEndian(now[0x10..]),
                needs = Items(now, IngredientSlots),
                gives = Items(now, GiveSlots),
                gave = Items(before[p], GiveSlots),
            });
        }
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        // Anything changed must be a known item. Untouched slots may hold key items the catalog doesn't list (Monographs).
        var catalog = ItemCatalog.Instance;
        var table = new BattlePack(context.Edit(BattlePack.UsPath)).Table(Section);
        var original = new BattlePack(context.Original.Extract(context.Original.Find(BattlePack.UsPath)!)).Table(Section);
        var problems = new List<string>();
        for (int p = 0; p < table.Count; p++)
        {
            var record = table.Record(p);
            var was = original.Record(p);
            foreach (int o in GiveSlots)
            {
                ushort item = BinaryPrimitives.ReadUInt16LittleEndian(record[o..]);
                bool changed = item != BinaryPrimitives.ReadUInt16LittleEndian(was[o..]);
                if (changed && catalog.Find(item) is null) problems.Add($"Bazaar package {p} gives an unknown item {item:X4}.");
            }
        }
        return problems;
    }
}
