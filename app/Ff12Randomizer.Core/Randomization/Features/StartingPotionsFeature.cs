using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Test mod: Vaan (party record 0) starts with 99 Potions (item 0x0000) instead of 5. A one-byte change that is easy to
/// see on a new game, so it proves the whole pipeline from settings to loader.
/// </summary>
public sealed class StartingPotionsFeature : IRandomizerFeature
{
    public const ushort Potion = 0x0000;
    public const byte Amount = 99;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn("potions");

    public void Apply(RandomizerContext context)
    {
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var vaan = new PartyMemberRecord(pack.Table(BattlePack.PartySection).Record(0));
        for (int slot = 0; slot < PartyMemberRecord.ItemSlots; slot++)
        {
            if (vaan.ItemId(slot) != Potion) continue;
            byte before = vaan.ItemAmount(slot);
            vaan.SetItemAmount(slot, Amount);
            context.Spoil("party", new { character = "Vaan", item = "Potion", before, after = Amount });
            return;
        }
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        var pack = new BattlePack(context.Edit(BattlePack.UsPath));
        var vaan = new PartyMemberRecord(pack.Table(BattlePack.PartySection).Record(0));
        bool found = false;
        for (int slot = 0; slot < PartyMemberRecord.ItemSlots; slot++)
        {
            found |= vaan.ItemId(slot) == Potion && vaan.ItemAmount(slot) == Amount;
        }
        return found ? [] : ["Vaan's starting Potions weren't where expected in battle_pack.bin."];
    }
}
