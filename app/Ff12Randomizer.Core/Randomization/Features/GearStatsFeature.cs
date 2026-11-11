using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Weapon and armor stats in battle_pack.bin section 13 (one 0x34 byte record per piece of equipment, record i = item
/// 0x1000 + i; field layout from Bartz24's FFModdingWPF, MIT, and checked against known items: Broadsword attack 14,
/// Gokuu Pole 72, Brave Suit defense 57). Weapons and ammunition: attack power at 0x1A. Armor: 0x18 and 0x19 (defense
/// and magick resist on body and head pieces, evade and magick evade on shields). Values are scaled around the original,
/// zeros stay zero, so a stat a piece doesn't have never appears. Equipment that isn't in the item list (enemy and guest
/// weapons) is left alone. Stats are read from the game data, so gear you already own changes too.
/// </summary>
public sealed class GearStatsFeature : IRandomizerFeature
{
    public const string Weapons = "wstats", Armor = "astats";
    public const int Section = 13, FirstEquipId = 0x1000;
    private static readonly int[] WeaponFields = [0x1A];
    private static readonly int[] ArmorFields = [0x18, 0x19];

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Weapons) || settings.IsOn(Armor);

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var table = pack.Table(Section);

        foreach (var (option, category, fields, labels) in new[]
                 {
                     (Weapons, ItemCategory.Weapon, WeaponFields, new[] { "attack" }),
                     (Armor, ItemCategory.Armor, ArmorFields, new[] { "defense", "magick resist" }),
                 })
        {
            if (!context.Settings.IsOn(option)) continue;
            var rng = context.RngFor(option);
            for (int i = 0; i < table.Count; i++)
            {
                if (catalog.Find((ushort)(FirstEquipId + i)) is not { Category: var c, Name: var name } || c != category) continue;
                var record = table.Record(i);
                var changes = new Dictionary<string, object>();
                for (int f = 0; f < fields.Length; f++)
                {
                    byte before = record[fields[f]];
                    if (before == 0) continue;
                    byte after = Scale(rng, before, sane);
                    record[fields[f]] = after;
                    // Body and head armor carry flag 0x40 at 0x07; shields don't, and use the same bytes for evade.
                    string label = category == ItemCategory.Armor && (record[0x07] & 0x40) == 0 ? (f == 0 ? "evade" : "magick evade") : labels[f];
                    if (after != before) changes[label] = RandomizerContext.Change(before, after);
                }
                if (changes.Count > 0) context.Spoil(option == Weapons ? "weapons" : "armor", new { item = name, stats = changes });
            }
        }
    }

    /// <summary>Within -25%/+30% of the original when sane, else between 40% and 200% of it; never 0 and at most 254.</summary>
    private static byte Scale(Rng rng, byte value, bool sane)
    {
        double factor = sane ? 0.75 + rng.NextDouble() * 0.55 : 0.4 + rng.NextDouble() * 1.6;
        return (byte)Math.Clamp((int)Math.Round(value * factor), 1, 254);
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        var table = new BattlePack(context.Edit(BattlePack.UsPath)).Table(Section);
        var original = new BattlePack(context.Original.Extract(context.Original.Find(BattlePack.UsPath)!)).Table(Section);
        var problems = new List<string>();
        for (int i = 0; i < table.Count; i++)
        {
            var now = table.Record(i);
            var was = original.Record(i);
            foreach (int f in WeaponFields.Concat(ArmorFields))
                if ((was[f] == 0) != (now[f] == 0)) problems.Add($"Equipment {FirstEquipId + i:X4} gained or lost stat 0x{f:X2}.");
        }
        return problems;
    }
}
