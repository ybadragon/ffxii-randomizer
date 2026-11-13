namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Experiment (CLI only, toggle "exp_opengates"), first step of our own open world: Rabanastre's gates are open from the
/// start of the game.
/// The boot script pre_z0000 derives the road closure state (savemem 0x040A) from the story flag on every map change:
/// "switch (story flag) { case lt(100): state = 2; default: state = 0; }", and the Rabanastre gate maps block their exits
/// while it isn't 0. Compiled, the two assignments are "PUSHII 2; POPV state; JMP end; PUSHII 0; POPV state; JMP end"
/// followed by the "PUSHII 100" of the comparison. The 2 becomes 0, so the state is always "open". One byte changes.
/// </summary>
public sealed class OpenGatesExperiment : IRandomizerFeature
{
    public const string Id = "exp_opengates";
    public const string BootScriptPath = "ps2data/plan_master/us/event/pre_z/pre_z0000/pre_z0000.ebp";
    private const byte PushII = 0x4F, PopV = 0x49, Jmp = 0x51;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id) && !settings.IsOn(OpenWorldFeature.Id);

    public void Apply(RandomizerContext context)
    {
        var b = context.Edit(BootScriptPath);
        var found = new List<int>();
        for (int p = 0; p + 21 <= b.Length; p++)
        {
            bool closed = b[p] == PushII && b[p + 1] == 2 && b[p + 2] == 0 && b[p + 3] == PopV && b[p + 6] == Jmp;
            bool open = b[p + 9] == PushII && b[p + 10] == 0 && b[p + 11] == 0 && b[p + 12] == PopV && b[p + 15] == Jmp;
            bool sameVariable = b[p + 4] == b[p + 13] && b[p + 5] == b[p + 14];
            bool comparedWith100 = b[p + 18] == PushII && b[p + 19] == 100 && b[p + 20] == 0;
            if (closed && open && sameVariable && comparedWith100) found.Add(p);
        }
        if (found.Count != 1)
            throw new InvalidDataException($"Open gates: expected the road closure state once in pre_z0000, found it {found.Count} times.");
        b[found[0] + 1] = 0;
        context.Spoil("experiments", new { change = "Rabanastre's gates are open from the start" });
    }
}
