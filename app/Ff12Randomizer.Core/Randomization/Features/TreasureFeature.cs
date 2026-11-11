using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Treasure chests: what each chest holds (common items, and the rare items found with the Diamond Armlet) and optionally
/// gil amounts. Chests that only ever give gil and slots holding key items or anything not shuffleable are left alone.
/// The game reads chest tables when you enter an area, so unopened chests in an existing save change too.
/// </summary>
public sealed class TreasureFeature : IRandomizerFeature
{
    public const string Id = "treasure", Gil = "treasuregil";
    public const string Shuffle = "shuffle", Random = "random";
    private const int Slots = 4;

    public bool IsEnabled(RandomizerSettings settings) => Mode(settings) != "orig" || settings.IsOn(Gil);

    private static string Mode(RandomizerSettings settings) =>
        settings.ChoiceOf(Randomization.OptionCatalog.AllGroups.First(g => g.Id == Id));

    private sealed record Chest(TreasureMap Map, int Index, TreasureRecord Record, TreasureGroup? Group)
    {
        public bool GivesItems => Record.GilChance < 100;
    }

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        string mode = Mode(context.Settings);
        var rng = context.RngFor(Id);

        // Load every chest; before[] remembers the original contents for the spoiler log.
        var chests = new List<Chest>();
        foreach (var map in TreasureData.Maps)
        {
            var table = new EbpTreasureTable(context.Edit(map.ArchivePath), map);
            for (int i = 0; i < table.Count; i++) chests.Add(new Chest(map, i, table[i], TreasureData.GroupOf(map.MapId, i)));
        }
        var before = chests.Select(c => (Items: Enumerable.Range(0, Slots).Select(c.Record.GetItem).ToArray(), c.Record.GilCommon, c.Record.GilRare)).ToList();

        bool Movable(ushort id) => catalog.Find(id) is { Shuffleable: true };

        if (mode == Shuffle)
        {
            // Common and rare (Diamond Armlet) slots shuffle separately. When sane, an item only trades places with items
            // of similar rank (loot with loot), so every chest keeps roughly the strength it had.
            foreach (bool rare in new[] { false, true })
            {
                var slots = chests.Where(c => c.GivesItems)
                    .SelectMany(c => Enumerable.Range(rare ? 2 : 0, 2).Select(s => (Chest: c, Slot: s)))
                    .Where(x => Movable(x.Chest.Record.GetItem(x.Slot)))
                    .ToList();
                foreach (var band in slots.GroupBy(x => ItemPools.SaneGroup(catalog.Find(x.Chest.Record.GetItem(x.Slot))!, sane)))
                {
                    var members = band.ToList();
                    var items = members.Select(x => x.Chest.Record.GetItem(x.Slot)).ToList();
                    rng.Shuffle(items);
                    for (int i = 0; i < members.Count; i++) members[i].Chest.Record.SetItem(members[i].Slot, items[i]);
                }
            }
        }
        else if (mode == Random)
        {
            var pool = ItemPools.Shuffleable(catalog);
            foreach (var chest in chests.Where(c => c.GivesItems))
            {
                for (int slot = 0; slot < Slots; slot++)
                {
                    if (catalog.Find(chest.Record.GetItem(slot)) is not { Shuffleable: true } original) continue;
                    chest.Record.SetItem(slot, ItemPools.PickLike(rng, pool, original, sane).Id);
                }
            }
        }

        if (context.Settings.IsOn(Gil))
        {
            var gilRng = context.RngFor(Gil);
            foreach (var chest in chests.Where(c => c.Record.GilChance > 0))
            {
                chest.Record.GilCommon = RollGil(gilRng, chest.Record.GilCommon, sane);
                chest.Record.GilRare = RollGil(gilRng, chest.Record.GilRare, sane);
            }
        }

        // Spoiler: places in the game's own names, ordered by area and place so it reads like a guide.
        var names = new MapNames(context.Original);
        var changed = new List<(string Area, string Place, int Chest, object Entry)>();
        for (int i = 0; i < chests.Count; i++)
        {
            var c = chests[i];
            var now = Enumerable.Range(0, Slots).Select(c.Record.GetItem).ToArray();
            if (now.SequenceEqual(before[i].Items) && c.Record.GilCommon == before[i].GilCommon && c.Record.GilRare == before[i].GilRare) continue;
            string area = c.Group?.Area ?? "";
            string place = names.NameOf(c.Map.MapId, c.Group?.Subarea) ?? c.Map.MapId;
            changed.Add((area, place, c.Index, new
            {
                area,
                place,
                map = c.Map.MapId,
                chest = c.Index,
                gilChance = c.Record.GilChance,
                common = new[] { catalog.NameOf(now[0]), catalog.NameOf(now[1]) },
                diamondArmlet = new[] { catalog.NameOf(now[2]), catalog.NameOf(now[3]) },
                gil = c.Record.GilChance > 0 ? new[] { (int)c.Record.GilCommon, c.Record.GilRare } : null,
                was = new
                {
                    common = new[] { catalog.NameOf(before[i].Items[0]), catalog.NameOf(before[i].Items[1]) },
                    diamondArmlet = new[] { catalog.NameOf(before[i].Items[2]), catalog.NameOf(before[i].Items[3]) },
                    gil = c.Record.GilChance > 0 ? new[] { (int)before[i].GilCommon, before[i].GilRare } : null,
                },
            }));
        }
        foreach (var entry in changed.OrderBy(e => e.Area, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.Place, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Chest))
            context.Spoil("treasure", entry.Entry);
    }

    private static ushort RollGil(Rng rng, ushort original, bool sane)
    {
        if (original == 0) return 0;
        if (sane) return (ushort)Math.Clamp(original * (0.5 + rng.NextDouble() * 1.5), 1, ushort.MaxValue);
        return (ushort)rng.Next(1, Math.Min(ushort.MaxValue, original * 5 + 1000));
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        foreach (var map in TreasureData.Maps)
        {
            var table = new EbpTreasureTable(context.Edit(map.ArchivePath), map);
            for (int i = 0; i < table.Count; i++)
            {
                for (int slot = 0; slot < Slots; slot++)
                {
                    if (catalog.Find(table[i].GetItem(slot)) is null)
                        yield return $"{map.MapId} chest {i} holds an unknown item {table[i].GetItem(slot):X4}.";
                }
            }
        }
    }
}
