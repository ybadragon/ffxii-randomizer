using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Buy prices (selling gives half). Consumables: battle_pack.bin section 18, one 0xC byte record per item 0x00-0x3F with the
/// price as a u16 at 0x0A, found by searching for the Zodiac Age prices of Potion 60, Hi-Potion 180 and Ether 222.
/// Equipment: section 13, u16 at 0x12 (Bartz24's FFModdingWPF, MIT). Magicks: section 29 and technicks: section 30, one 8 byte
/// record per item 0x3000+i / 0x4000+i with the price as a u16 at 0x00 (byte 0x04 is the sequential ability index, 0 and 158;
/// Cure 200, Vox 300, First Aid 600, Charge 1500). Prices of
/// 0 or 1 (things that aren't sold) don't change. Prices are read when you shop, so existing saves see the change.
/// </summary>
public sealed class ShopPricesFeature : IRandomizerFeature
{
    public const string Id = "shopprices";
    public const int ItemSection = 18, ItemPriceOffset = 0x0A;
    public const int EquipSection = GearStatsFeature.Section, EquipPriceOffset = 0x12;
    public const int MagickSection = 29, TechnickSection = 30, AbilityPriceOffset = 0x00;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        var rng = context.RngFor(Id);
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));

        foreach (var (section, offset, firstId) in new[] { (ItemSection, ItemPriceOffset, 0x0000), (EquipSection, EquipPriceOffset, GearStatsFeature.FirstEquipId),
            (MagickSection, AbilityPriceOffset, 0x3000), (TechnickSection, AbilityPriceOffset, 0x4000) })
        {
            var table = pack.Table(section);
            for (int i = 0; i < table.Count; i++)
            {
                if (catalog.Find((ushort)(firstId + i)) is not { Shuffleable: true } item) continue;
                var record = table.Record(i);
                ushort before = BinaryPrimitives.ReadUInt16LittleEndian(record[offset..]);
                if (before <= 1) continue;
                ushort after = Roll(rng, before, sane);
                BinaryPrimitives.WriteUInt16LittleEndian(record[offset..], after);
                if (after != before) context.Spoil("prices", new { item = item.Name, price = RandomizerContext.Change(before, after) });
            }
        }
    }

    /// <summary>Half to one and a half times the original when sane, else a quarter to three times; rounded like shop prices.</summary>
    private static ushort Roll(Rng rng, ushort price, bool sane)
    {
        double factor = sane ? 0.5 + rng.NextDouble() : 0.25 + rng.NextDouble() * 2.75;
        double value = price * factor;
        double step = value >= 1000 ? 50 : value >= 100 ? 10 : 1;
        return (ushort)Math.Clamp(Math.Round(value / step) * step, 2, ushort.MaxValue);
    }
}
