using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// What each party member and guest brings when they join (battle_pack.bin section 16, 0x80 bytes per record):
/// equipment as five u16 ids at 0x0A (weapon, off-hand, head, body, accessory) and up to ten items (ids at 0x58, amounts at
/// 0x34). Gear is only replaced by gear of the same class: the same armor/off-hand/accessory flags (equipment record byte
/// 0x07), category (0x09) and icon (0x04, which tells light, mystic and heavy armor apart, as in Bartz24's FFModdingWPF), so
/// a bow user still gets a bow and arrows and light armor stays light.
/// Consumables are replaced by consumables with the same amount; magicks, technicks, gambits and key items stay. The
/// character's name is battle text table 8 at the record's byte 0x30 (Vaan's record has 1, Fran's 3, Balthier's 2, matching
/// their Dagger, Shortbow and Altair). Given when a character joins, so it needs a new game.
/// </summary>
public sealed class StartingInventoryFeature : IRandomizerFeature
{
    public const string Id = "startinv";
    private const int GearOffset = 0x0A, GearSlots = 5, NameOffset = 0x30;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        bool keepPotions = context.Settings.IsOn("potions");
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

        for (int r = 0; r < party.Count; r++)
        {
            var before = party.Record(r).ToArray();
            var record = party.Record(r);
            for (int g = 0; g < GearSlots; g++)
            {
                ushort id = BinaryPrimitives.ReadUInt16LittleEndian(record[(GearOffset + g * 2)..]);
                if (catalog.Find(id) is not { Shuffleable: true } original || ClassOf(id) is not { } wanted) continue;
                var pick = ItemPools.PickLike(rng, gearPool, original, sane, allowed: i => ClassOf(i.Id) == wanted);
                BinaryPrimitives.WriteUInt16LittleEndian(record[(GearOffset + g * 2)..], pick.Id);
            }
            for (int s = 0; s < PartyMemberRecord.ItemSlots; s++)
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
                    was = new { equipment = Gear(before), items = Items(before) },
                });
            }
        }

        string[] Gear(byte[] rec) => Enumerable.Range(0, GearSlots)
            .Select(g => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(GearOffset + g * 2)))
            .Where(id => id != 0xFFFF).Select(catalog.NameOf).ToArray();
        string[] Items(byte[] rec) => Enumerable.Range(0, PartyMemberRecord.ItemSlots)
            .Where(s => rec[0x34 + s] > 0 && catalog.Find(BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(0x58 + s * 2))) is { Category: ItemCategory.Item })
            .Select(s => $"{catalog.NameOf(BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(0x58 + s * 2)))} x{rec[0x34 + s]}").ToArray();
    }
}
