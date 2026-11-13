namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Developer test switch "exp_override=&lt;folder&gt;": each &lt;name&gt;.ebp in the folder replaces the US script of that name,
/// to try out scripts rebuilt with ffgriever's assembler in the game. Never part of a normal seed.
/// </summary>
public sealed class OverrideFilesExperiment : IRandomizerFeature
{
    public const string Id = "exp_override";

    public bool IsEnabled(RandomizerSettings settings) => settings.Choices.ContainsKey(Id);

    public void Apply(RandomizerContext context)
    {
        string folder = context.Settings.Choices[Id];
        var paths = context.Original.Entries.Where(e => e.Name.StartsWith("ps2data/plan_master/us/") && e.Name.EndsWith(".ebp"))
            .GroupBy(e => Path.GetFileName(e.Name)).ToDictionary(g => g.Key, g => g.First().Name);
        foreach (string file in Directory.GetFiles(folder, "*.ebp"))
        {
            string path = paths[Path.GetFileName(file)];
            context.Replace(path, File.ReadAllBytes(file));
            context.Spoil("override", new { file = path });
        }
    }
}
