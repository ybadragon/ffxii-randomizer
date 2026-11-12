using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// What each party member (not guests: they keep their own, partly unnamed, gear) brings when they join (battle_pack.bin section 16, 0x80 bytes per record):
/// equipment as five u16 ids at 0x0A (weapon, off-hand, head, body, accessory) and up to ten items (ids at 0x58, amounts at
/// 0x34). Gear is only replaced by gear of the same class: the same armor/off-hand/accessory flags (equipment record byte
/// 0x07), category (0x09) and icon (0x04, which tells light, mystic and heavy armor apart, as in Bartz24's FFModdingWPF), so
/// a bow user still gets a bow and arrows and light armor stays light.
/// When the new piece needs a license the party member doesn't start with (<see cref="StartingLicenses"/>; the game
/// takes such gear off), it goes in the inventory instead and the slot starts empty. With "Starting items too", consumables are replaced by consumables with the same amount; magicks, technicks,
/// gambits and key items always stay. The
/// character's name is battle text table 8 at the record's byte 0x30 (Vaan's record has 1, Fran's 3, Balthier's 2, matching
/// their Dagger, Shortbow and Altair). Given when a character joins, so it needs a new game.
/// </summary>
public sealed class StartingInventoryFeature : IRandomizerFeature
{
    public const string Id = "startinv";
    public const string ItemsId = "startitems";
    public const string LicensesId = "startlicenses";
    private const int GearOffset = 0x0A, GearSlots = 5, NameOffset = 0x30, ItemIdsOffset = 0x58, ItemAmountsOffset = 0x34;

    /// <summary>An unused item slot (id 0xFFFF, amount 0), or null when all ten are taken.</summary>
    private static int? FreeItemSlot(Span<byte> record)
    {
        for (int s = 0; s < PartyMemberRecord.ItemSlots; s++)
        {
            if (record[ItemAmountsOffset + s] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(record[(ItemIdsOffset + s * 2)..]) == 0xFFFF) return s;
        }
        return null;
    }

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        bool keepPotions = context.Settings.IsOn("potions");
        bool swapItems = context.Settings.IsOn(ItemsId);
        bool giveLicenses = context.Settings.IsOn(LicensesId);
        var rng = context.RngFor(Id);
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var party = pack.Table(BattlePack.PartySection);
        var equipment = pack.Table(GearStatsFeature.Section);

        (byte Flags, byte Type, byte Icon)? ClassOf(ushort id)
        {
            int index = id - GearStatsFeature.FirstEquipId;
            if (index < 0 || index >= equipment.Count) return null;
            var record = equipment.Record(index);
            return ((byte)(record[0x07] & 0xF0), record[0x09], record[0x04]);
        }

        var gearPool = ItemPools.Shuffleable(catalog)
            .Where(i => i.Category is ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Accessory).ToList();
        var itemPool = ItemPools.Shuffleable(catalog).Where(i => i.Category == ItemCategory.Item && i.Id != 0).ToList();
        var licenses = StartingLicenses.Read(pack);

        for (int r = 0; r < party.Count; r++)
        {
            // Guests (Vossler, Larsa, Reddas...) keep their own gear: some of it is guest-only and has no name in the game.
            if (!licenses.IsPartyMember(r)) continue;
            var before = party.Record(r).ToArray();
            var record = party.Record(r);
            var given = new List<string>();
            for (int g = 0; g < GearSlots; g++)
            {
                ushort id = BinaryPrimitives.ReadUInt16LittleEndian(record[(GearOffset + g * 2)..]);
                if (catalog.Find(id) is not { Shuffleable: true } original || ClassOf(id) is not { } wanted) continue;
                var pick = ItemPools.PickLike(rng, gearPool, original, sane, allowed: i => ClassOf(i.Id) == wanted);
                if (giveLicenses && !licenses.CanStartWith(r, pick.Id) && licenses.Grant(pack, r, pick.Id) is int license)
                {
                    // "Give the licenses for it": the member starts with the license, so the piece can be worn.
                    given.Add(pack.Text(LicenseBoardsFeature.LicenseNamesTable, license) ?? $"License {license}");
                }
                if (licenses.CanStartWith(r, pick.Id))
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(record[(GearOffset + g * 2)..], pick.Id);
                }
                else if (FreeItemSlot(record) is int slot)
                {
                    // The game takes away equipped gear without its license, so the new piece goes in the inventory
                    // instead and the slot is left empty: the original piece is replaced either way.
                    BinaryPrimitives.WriteUInt16LittleEndian(record[(ItemIdsOffset + slot * 2)..], pick.Id);
                    record[ItemAmountsOffset + slot] = 1;
                    BinaryPrimitives.WriteUInt16LittleEndian(record[(GearOffset + g * 2)..], 0xFFFF);
                }
            }
            // Consumables only with "Starting items too"; magicks, technicks, gambits and key items always stay.
            for (int s = 0; swapItems && s < PartyMemberRecord.ItemSlots; s++)
            {
                var member = new PartyMemberRecord(record);
                ushort id = member.ItemId(s);
                if (member.ItemAmount(s) == 0 || catalog.Find(id) is not { Shuffleable: true, Category: ItemCategory.Item } original) continue;
                if (keepPotions && r == 0 && id == StartingPotionsFeature.Potion) continue;
                var pick = ItemPools.PickLike(rng, itemPool, original, sane);
                BinaryPrimitives.WriteUInt16LittleEndian(record[(0x58 + s * 2)..], pick.Id);
            }

            if (!record.SequenceEqual(before))
            {
                context.Spoil("startingInventory", new
                {
                    character = pack.Text(BattlePack.NamesTable, record[NameOffset]) ?? $"Party record {r}",
                    equipment = Gear(record.ToArray()),
                    items = Items(record.ToArray()),
                    licensesGiven = given.Count > 0 ? given : null,
                    was = new { equipment = Gear(before), items = Items(before) },
                });
            }
        }

        string[] Gear(byte[] rec) => Enumerable.Range(0, GearSlots)
            .Select(g => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(GearOffset + g * 2)))
            .Where(id => id != 0xFFFF).Select(catalog.NameOf).ToArray();
        string[] Items(byte[] rec) => Enumerable.Range(0, PartyMemberRecord.ItemSlots)
            .Where(s => rec[0x34 + s] > 0 && catalog.Find(BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(0x58 + s * 2))) is { Category: ItemCategory.Item or ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Accessory })
            .Select(s => $"{catalog.NameOf(BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(0x58 + s * 2)))} x{rec[0x34 + s]}").ToArray();
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var party = pack.Table(BattlePack.PartySection);
        var licenses = StartingLicenses.Read(pack);
        for (int r = 0; r < party.Count; r++)
        {
            var record = party.Record(r).ToArray();
            for (int g = 0; g < GearSlots; g++)
            {
                ushort id = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(GearOffset + g * 2));
                if (id != 0xFFFF && !licenses.CanStartWith(r, id))
                    yield return $"Starting equipment: party record {r} would start with {ItemCatalog.Instance.NameOf(id)} without its license, and the game would take it away.";
            }
        }
    }
}

/// <summary>
/// The licenses party members hold from the start, from the license table (battle_pack section 12, 0x18 bytes per license):
/// byte 0x07 is a mask of the party records (bit n = section 16 record n) that start with the license, and the u16s from
/// 0x08 are the items it allows (0xFFFF = none). Dagger and Light Armor 1 have Vaan's bit, which is why he can wear his
/// Dagger, Leather Cap and Leather Clothing before any board. Items in no license (ammunition, guest-only gear) need none.
/// Records in no mask (guests) aren't limited: their vanilla gear is outside their masks too.
/// </summary>
public sealed class StartingLicenses
{
    public const int LicenseSection = 12, MaskOffset = 0x07, ItemsOffset = 0x08, ItemSlots = 8;
    private readonly HashSet<ushort> _licensed = [];
    private readonly Dictionary<ushort, int> _firstLicenseOf = [];
    private readonly Dictionary<int, HashSet<ushort>> _byMember = [];

    public static StartingLicenses Read(BattlePack pack)
    {
        var result = new StartingLicenses();
        var table = pack.Table(LicenseSection);
        for (int i = 0; i < table.Count; i++)
        {
            var record = table.Record(i).ToArray();
            var items = Enumerable.Range(0, ItemSlots)
                .Select(s => BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(ItemsOffset + s * 2)))
                .Where(id => id != 0xFFFF).ToList();
            result._licensed.UnionWith(items);
            foreach (var id in items) result._firstLicenseOf.TryAdd(id, i);
            for (int bit = 0; bit < 8; bit++)
            {
                if ((record[MaskOffset] & (1 << bit)) == 0) continue;
                if (!result._byMember.TryGetValue(bit, out var set)) result._byMember[bit] = set = [];
                set.UnionWith(items);
            }
        }
        return result;
    }

    /// <summary>Whether party record <paramref name="member"/> is a party member (it starts with some license), not a guest.</summary>
    public bool IsPartyMember(int member) => _byMember.ContainsKey(member);

    /// <summary>
    /// Makes party record <paramref name="member"/> start with the first license that allows <paramref name="item"/>, by
    /// setting its bit in that license's starting mask. Returns the license index, or null when none applies.
    /// </summary>
    public int? Grant(BattlePack pack, int member, ushort item)
    {
        if (member is < 0 or > 7 || !_firstLicenseOf.TryGetValue(item, out int license)) return null;
        var record = pack.Table(LicenseSection).Record(license);
        record[MaskOffset] |= (byte)(1 << member);
        var bytes = record.ToArray();
        var items = Enumerable.Range(0, ItemSlots)
            .Select(s => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ItemsOffset + s * 2)))
            .Where(id => id != 0xFFFF);
        if (!_byMember.TryGetValue(member, out var set)) _byMember[member] = set = [];
        set.UnionWith(items);
        return license;
    }

    /// <summary>Whether party record <paramref name="member"/> keeps <paramref name="item"/> if it starts with it.</summary>
    public bool CanStartWith(int member, ushort item) =>
        !_licensed.Contains(item) || !_byMember.TryGetValue(member, out var own) || own.Contains(item);
}
