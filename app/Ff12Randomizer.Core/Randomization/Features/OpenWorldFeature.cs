using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Open world, from Bartz24's FF12 Open World Randomizer (MIT, base files from FFModdingWPF commit f20d432):
/// <list type="bullet">
/// <item>790 edited map and event scripts that replace story gates with key item checks; story events hand out rewards
/// from battle_pack.bin section 37 (reward id 0x9000 + record index; record = u32 gil, two u16 item/amount pairs).</item>
/// <item>battle_pack sections 37 (rewards, holding his default key item layout), 28 (prices: record 0x76 must encode the
/// party order Vaan, Balthier, Fran, Basch, Ashe, Penelo, which the scripts read), 32 (stops the custom key items being
/// sold) and section 61 text tables 11 and 12 (names of the custom key items).</item>
/// <item>the menu and help text files that describe them.</item>
/// <item>his hunt rewards patch (Espers and the Second Board as rewards), done natively by our loader.</item>
/// </list>
/// Sections Bartz24 changed for his other options (equipment 13, augments 58, 35, bazaar 57, licenses 12, party 16, text
/// tables 0, 1, 3) stay vanilla, and the party order is never shuffled, which keeps Insurgent's Manifesto unnecessary.
/// Runs before every other feature so they work on top of the open world files. Needs a new game.
/// </summary>
public sealed class OpenWorldFeature : IRandomizerFeature
{
    public const string Id = "exp_bartzopenworld";
    public const string HuntRewardsLoaderFeature = "hunt_rewards";
    public const int RewardsSection = 37, RewardSize = 0xC;
    public const int PricesSection = 28, PartyOrderRecord = 0x76, StartingBoardsRecord = 0x77;
    private static readonly int[] Sections = [37, 28, 32];
    private static readonly int[] TextTables = [11, 12];

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    public void Apply(RandomizerContext context)
    {
        var world = OpenWorldBase.Load();

        foreach (var (path, bytes) in world.Files)
        {
            if (context.Original.Find(path) is null) continue; // pieces of packed files the loader can't serve on their own
            context.Replace(path, bytes);
        }

        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var section61 = BattlePack.Repack(pack.Section(BattlePack.TextSection),
            TextTables.Where(world.TextTables.ContainsKey).ToDictionary(t => t, t => world.TextTables[t]), alignment: 4);
        var sections = Sections.Where(world.Sections.ContainsKey).ToDictionary(s => s, s => world.Sections[s]);
        sections[BattlePack.TextSection] = section61;
        context.Replace(BattlePack.UsPath, BattlePack.Repack(pack.Data, sections));

        // The new game script (naf_a01) reads each character's starting license board from price record 0x77 and writes
        // it through a save memory window the unmodded game rejects. 0 = no preset boards, so that write never runs.
        var prices = new BattlePack(context.Edit(BattlePack.UsPath)).Table(PricesSection);
        BinaryPrimitives.WriteUInt32LittleEndian(prices.Record(StartingBoardsRecord), 0);

        context.LoaderFeatures.Add(HuntRewardsLoaderFeature);
        SpoilRewards(context);
    }

    /// <summary>Where every reward is, named by data/rewards.csv (Bartz24's FFModdingWPF, MIT): area and event.</summary>
    private static void SpoilRewards(RandomizerContext context)
    {
        var catalog = ItemCatalog.Instance;
        var table = new BattlePack(context.Edit(BattlePack.UsPath)).Table(RewardsSection);
        foreach (var row in Csv.ReadEmbedded("rewards.csv").Skip(1))
        {
            if (row.Count < 3 || !int.TryParse(row[2], System.Globalization.NumberStyles.HexNumber, null, out int id)) continue;
            int index = id - 0x9000;
            if (index < 0 || index >= table.Count) continue;
            var record = table.Record(index);
            uint gil = BinaryPrimitives.ReadUInt32LittleEndian(record);
            var items = new List<string>();
            foreach (int offset in new[] { 0x4, 0x8 })
            {
                ushort item = BinaryPrimitives.ReadUInt16LittleEndian(record[offset..]);
                ushort amount = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 2)..]);
                if (item != 0xFFFF && amount > 0) items.Add(amount > 1 ? $"{catalog.NameOf(item)} x{amount}" : catalog.NameOf(item));
            }
            if (gil == 0 && items.Count == 0) continue;
            context.Spoil("openWorldRewards", new { area = row[0], check = row[1], gil = gil > 0 ? gil : (uint?)null, items });
        }
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        // The scripts read the party order from price record 0x76 (must be the vanilla order) and starting boards from 0x77.
        var prices = new BattlePack(context.Edit(BattlePack.UsPath)).Table(PricesSection);
        uint order = BinaryPrimitives.ReadUInt32LittleEndian(prices.Record(PartyOrderRecord));
        if (order != 44790) yield return $"Open world: party order value is {order}, expected 44790 (Vaan, Balthier, Fran, Basch, Ashe, Penelo).";
        uint boards = BinaryPrimitives.ReadUInt32LittleEndian(prices.Record(StartingBoardsRecord));
        if (boards != 0) yield return $"Open world: starting board value is {boards}, expected 0 (no preset boards).";
    }
}
