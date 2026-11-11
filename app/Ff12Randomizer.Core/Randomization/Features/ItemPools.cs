using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>Shared rules for picking replacement items.</summary>
internal static class ItemPools
{
    /// <summary>Rank bands used by Keep things sane, so early areas don't get endgame items: 1-2, 3-4, 5-6, 7-8, 9+.</summary>
    public static int Band(int rank) => Math.Clamp((rank - 1) / 2, 0, 4);

    /// <summary>
    /// Which items may trade places under Keep things sane: loot only with loot (it is unranked), everything else with
    /// items in the same rank band. Without sane limits everything shares one group.
    /// </summary>
    public static int SaneGroup(ItemInfo item, bool sane) =>
        !sane ? 0 : item.Category == ItemCategory.Loot ? -1 : Band(item.Rank);

    /// <summary>A replacement for <paramref name="original"/>: loot for loot and similar rank for the rest when sane.</summary>
    public static ItemInfo PickLike(Rng rng, IReadOnlyList<ItemInfo> pool, ItemInfo original, bool sane, int rankBonus = 0)
    {
        if (sane && original.Category == ItemCategory.Loot)
            return PickNear(rng, pool, 0, sane: false, allowed: i => i.Category == ItemCategory.Loot);
        return PickNear(rng, pool, original.Rank + rankBonus, sane, allowed: sane ? i => i.Category != ItemCategory.Loot : null);
    }

    /// <summary>Items that may be handed out by a randomizer at all.</summary>
    public static IReadOnlyList<ItemInfo> Shuffleable(ItemCatalog catalog) =>
        catalog.All.Where(i => i.Shuffleable).OrderBy(i => i.Id).ToList();

    /// <summary>
    /// A random item from <paramref name="pool"/> with a rank within <paramref name="spread"/> of <paramref name="rank"/>,
    /// widening the window until something qualifies. Without sane limits any item in the pool can come up.
    /// </summary>
    public static ItemInfo PickNear(Rng rng, IReadOnlyList<ItemInfo> pool, int rank, bool sane, int spread = 1,
        Func<ItemInfo, bool>? allowed = null)
    {
        var candidates = pool.Where(i => allowed?.Invoke(i) ?? true).ToList();
        if (!sane || candidates.Count == 0) return rng.Pick(candidates.Count > 0 ? candidates : pool);
        for (int window = spread; window <= 10; window++)
        {
            var near = candidates.Where(i => Math.Abs(i.Rank - rank) <= window).ToList();
            if (near.Count > 0) return rng.Pick(near);
        }
        return rng.Pick(candidates);
    }
}
