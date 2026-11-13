using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Developer test switch "exp_level=&lt;n&gt;": the six main characters start at level n (byte 0x2E of their battle_pack
/// section 16 record), so a test party can survive a boss fight. Never part of a normal seed.
/// </summary>
public sealed class StartLevelExperiment : IRandomizerFeature
{
    public const string Id = "exp_level";

    public bool IsEnabled(RandomizerSettings settings) => settings.Choices.ContainsKey(Id);

    public void Apply(RandomizerContext context)
    {
        byte level = byte.Parse(context.Settings.Choices[Id]);
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var party = pack.Table(BattlePack.PartySection);
        for (int r = 0; r < party.Count; r++)
            if (party.Record(r)[0x14] < 6) party.Record(r)[0x2E] = level;
        context.Spoil("level", new { level });
    }
}
