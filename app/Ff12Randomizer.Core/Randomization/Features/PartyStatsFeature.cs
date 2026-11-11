namespace Ff12Randomizer.Core.Randomization.Features;

using Ff12Randomizer.Core.GameData;

/// <summary>
/// Shuffles starting stats between the six main characters: each takes another's whole stat block (bytes 0x16-0x2B of
/// their battle_pack.bin section 16 record, which sit between the character index at 0x14 and the level data at 0x2C and
/// hold base values and growth per stat). Moving whole blocks keeps every value one the game already uses, so nothing
/// depends on knowing exactly which byte is which stat. The six are the records whose character index (0x14) is 0-5.
/// Stats are set when a character joins, so this needs a new game.
/// </summary>
public sealed class PartyStatsFeature : IRandomizerFeature
{
    public const string Id = "pstats";
    private const int CharacterIndexOffset = 0x14, StatsStart = 0x16, StatsLength = 0x2C - 0x16, NameOffset = 0x30;

    public bool IsEnabled(RandomizerSettings settings) => settings.ChoiceOf(OptionCatalog.AllGroups.First(g => g.Id == Id)) == "shuffle";

    public void Apply(RandomizerContext context)
    {
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var party = pack.Table(BattlePack.PartySection);
        var mains = Enumerable.Range(0, party.Count).Where(r => party.Record(r)[CharacterIndexOffset] < 6)
            .OrderBy(r => party.Record(r)[CharacterIndexOffset]).ToList();
        if (mains.Count != 6) throw new InvalidDataException($"Expected the six main characters in section 16, found {mains.Count}.");

        var blocks = mains.Select(r => party.Record(r).Slice(StatsStart, StatsLength).ToArray()).ToList();
        var order = Enumerable.Range(0, 6).ToList();
        context.RngFor(Id).Shuffle(order);
        string Name(int record) => pack.Text(BattlePack.NamesTable, party.Record(record)[NameOffset]) ?? $"Party record {record}";

        for (int i = 0; i < 6; i++)
        {
            blocks[order[i]].CopyTo(party.Record(mains[i]).Slice(StatsStart, StatsLength));
            context.Spoil("partyStats", new { character = Name(mains[i]), hasStatsOf = Name(mains[order[i]]) });
        }
    }
}
