using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Shop stock (battle_pack.bin section 39). Items only change within their kind (weapon, armor, accessory, item,
/// magick/technick), each shop keeps its number of items per story stage, and gambits are left alone. With Keep things sane,
/// every magick and technick on sale somewhere stays on sale somewhere. Shops are read when opened, so existing saves see it.
/// </summary>
public sealed class ShopsFeature : IRandomizerFeature
{
    public const string Id = "shops";
    public const string Shuffle = "shuffle", Random = "random";

    public bool IsEnabled(RandomizerSettings settings) => Mode(settings) != "orig";

    private static string Mode(RandomizerSettings settings) =>
        settings.ChoiceOf(OptionCatalog.AllGroups.First(g => g.Id == Id));

    private sealed record Slot(ShopTable.ShopStock Shop, int Stage, ShopTable.StockList List, int Index, ItemInfo Original);

    public void Apply(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        bool sane = context.Settings.IsOn("sane");
        string mode = Mode(context.Settings);
        var rng = context.RngFor(Id);
        var table = new ShopTable(new BattlePack(context.Edit(BattlePack.UsPath)));

        var slots = new List<Slot>();
        foreach (var shop in table.Shops)
        {
            for (int stage = 0; stage < shop.Lists.Count; stage++)
            {
                var list = shop.Lists[stage];
                for (int i = 0; i < list.Count; i++)
                {
                    if (catalog.Find(list[i]) is { Shuffleable: true } item) slots.Add(new Slot(shop, stage, list, i, item));
                }
            }
        }

        // Shuffle (and, with sane on, magicks/technicks in Randomize mode) move the existing stock around.
        bool ShufflesHere(Slot s) => mode == Shuffle || (sane && s.Original.Category == ItemCategory.Ability);
        foreach (var group in slots.Where(ShufflesHere).GroupBy(s => (s.Original.Category, Band: sane ? ItemPools.Band(s.Original.Rank) : 0)))
        {
            var members = group.ToList();
            var items = members.Select(s => s.Original.Id).ToList();
            rng.Shuffle(items);
            for (int i = 0; i < members.Count; i++) members[i].List[members[i].Index] = items[i];
        }

        if (mode == Random)
        {
            var pool = ItemPools.Shuffleable(catalog);
            foreach (var slot in slots.Where(s => !ShufflesHere(s)))
            {
                var taken = StockOf(slot.Shop);
                var pick = ItemPools.PickNear(rng, pool, slot.Original.Rank, sane, spread: 1,
                    allowed: i => i.Category == slot.Original.Category && !taken.Contains(i.Id));
                slot.List[slot.Index] = pick.Id;
            }
        }

        RemoveDuplicates(slots, rng);

        foreach (var shop in table.Shops)
        {
            var stages = shop.Lists.Select((list, stage) => new
            {
                stage,
                now = list.Items.Select(catalog.NameOf).ToArray(),
                was = slots.Where(s => s.Shop == shop && s.Stage == stage).Select(s => s.Original.Name).ToArray(),
            }).Where(s => s.was.Length > 0).ToList();
            if (stages.Count == 0) continue;
            context.Spoil("shops", new { stockList = shop.Index, listedAs = shop.Info?.Name, location = shop.Info?.Location, stages });
        }
    }

    /// <summary>A shop selling the same item twice (in any stage) swaps one copy with a same-kind item from another shop.</summary>
    private static void RemoveDuplicates(List<Slot> slots, Rng rng)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;
            foreach (var shopSlots in slots.GroupBy(s => s.Shop))
            {
                var seen = new HashSet<ushort>();
                foreach (var slot in shopSlots)
                {
                    ushort id = slot.List[slot.Index];
                    if (seen.Add(id)) continue;
                    var kind = ItemCatalog.Instance.Find(id)?.Category;
                    var partners = slots.Where(o => o.Shop != slot.Shop && ItemCatalog.Instance.Find(o.List[o.Index])?.Category == kind
                        && !StockOf(slot.Shop).Contains(o.List[o.Index]) && !StockOf(o.Shop).Contains(id)).ToList();
                    if (partners.Count == 0) continue;
                    var other = rng.Pick(partners);
                    (slot.List[slot.Index], other.List[other.Index]) = (other.List[other.Index], id);
                    seen.Add(slot.List[slot.Index]);
                    changed = true;
                }
            }
            if (!changed) break;
        }
    }

    private static HashSet<ushort> StockOf(ShopTable.ShopStock shop) => shop.Lists.SelectMany(l => l.Items).ToHashSet();

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        var table = new ShopTable(new BattlePack(context.Edit(BattlePack.UsPath)));
        foreach (var shop in table.Shops)
        {
            foreach (var id in shop.Lists.SelectMany(l => l.Items))
            {
                if (id != 0xFFFF && id < 0x6000 && catalog.Find(id) is null) yield return $"{shop.Name} sells an unknown item {id:X4}.";
            }
        }
    }
}
