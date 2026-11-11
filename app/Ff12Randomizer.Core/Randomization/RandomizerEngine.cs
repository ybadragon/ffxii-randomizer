using System.Text.Json.Nodes;
using Ff12Randomizer.Core.Randomization.Features;
using Ff12Randomizer.Core.Vbf;

namespace Ff12Randomizer.Core.Randomization;

public sealed record RunResult(
    Seed Seed,
    RandomizerSettings Settings,
    IReadOnlyDictionary<string, byte[]> Files,
    JsonObject Spoiler,
    IReadOnlyList<string> Problems);

public sealed class ValidationFailedException(IReadOnlyList<string> problems)
    : Exception($"The randomizer produced something that failed its checks, so nothing was written to the game. First problem: {problems[0]}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

public static class RandomizerEngine
{
    /// <summary>Features in the order they run. Order is part of what a seed reproduces, so only ever append.</summary>
    public static IReadOnlyList<IRandomizerFeature> Features { get; } =
    [
        new StartingPotionsFeature(),
        new TreasureFeature(),
        new ShopsFeature(),
        new EnemyRewardsFeature(),
        new BazaarFeature(),
        new GearStatsFeature(),
        new EnemyStatsFeature(),
    ];

    public static RunResult Run(VbfArchive original, RandomizerSettings settings, Seed seed)
    {
        var context = new RandomizerContext(original, settings, seed);
        var enabled = Features.Where(f => f.IsEnabled(settings)).ToList();
        foreach (var feature in enabled) feature.Apply(context);
        var problems = enabled.SelectMany(f => f.Validate(context)).Distinct().ToList();
        foreach (var (name, bytes) in context.ChangedFiles)
        {
            var entry = original.Find(name);
            if (entry is null) problems.Add($"{name} is not in the game archive.");
            else if (Math.Max(1, (bytes.Length + VbfArchive.BlockSize - 1L) / VbfArchive.BlockSize) > Math.Max(1, entry.BlockCount))
                problems.Add($"{name} grew past its original size limit ({entry.BlockCount} × 64 KB).");
        }
        return new RunResult(seed, settings.Clone(), context.ChangedFiles, context.Spoiler, problems);
    }
}
