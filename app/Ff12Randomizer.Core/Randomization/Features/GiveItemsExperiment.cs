using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Developer test switch "exp_give=&lt;hex id&gt;:&lt;count&gt;,...": Vaan starts with these items in place of his first
/// starting item slots (for example Teleport Stones, 2000). Never part of a normal seed.
/// </summary>
public sealed class GiveItemsExperiment : IRandomizerFeature
{
    public const string Id = "exp_give";

    public bool IsEnabled(RandomizerSettings settings) => settings.Choices.ContainsKey(Id);

    public void Apply(RandomizerContext context)
    {
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var vaan = pack.Table(BattlePack.PartySection).Record(0);
        int slot = 0;
        foreach (string part in context.Settings.Choices[Id].Split(';'))
        {
            var bits = part.Split(':');
            BinaryPrimitives.WriteUInt16LittleEndian(vaan[(0x58 + slot * 2)..], Convert.ToUInt16(bits[0], 16));
            vaan[0x34 + slot] = byte.Parse(bits[1]);
            context.Spoil("give", new { item = bits[0], count = bits[1] });
            slot++;
        }
    }
}
