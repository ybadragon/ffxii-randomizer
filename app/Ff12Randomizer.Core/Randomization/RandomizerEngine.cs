using Ff12Randomizer.Core.GameData;
using System.Text.Json.Nodes;
using Ff12Randomizer.Core.Randomization.Features;
using Ff12Randomizer.Core.Vbf;

namespace Ff12Randomizer.Core.Randomization;

public sealed record RunResult(
    Seed Seed,
    RandomizerSettings Settings,
    IReadOnlyDictionary<string, byte[]> Files,
    JsonObject Spoiler,
    IReadOnlyList<string> Problems,
    IReadOnlyCollection<string>? LoaderFeatures = null);

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
        // First: the open world replaces whole files that the other features then edit. It uses no random numbers, so
        // running it first doesn't change what any seed produces without it.
        new OpenWorldFeature(),
        new StartingPotionsFeature(),
        new TreasureFeature(),
        new ShopsFeature(),
        new EnemyRewardsFeature(),
        new BazaarFeature(),
        new GearStatsFeature(),
        new EnemyStatsFeature(),
        new ElementsFeature(),
        new ShopPricesFeature(),
        new LicenseBoardsFeature(),
        new StartingInventoryFeature(),
        new PartyStatsFeature(),
        new SkipPrologueFeature(),
        new EnemySwapExperiment(),
        new EnemyLocationsFeature(),
        new OpenGatesExperiment(),
        new OpenRoadsFeature(),
        new OverrideFilesExperiment(),
        new WeakEnemiesExperiment(),
        new StartLevelExperiment(),
        new GiveItemsExperiment(),
    ];

    public static RunResult Run(VbfArchive original, RandomizerSettings settings, Seed seed)
    {
        var context = new RandomizerContext(original, settings, seed);
        var enabled = Features.Where(f => f.IsEnabled(settings)).ToList();
        // Names items.csv lacks come from the battle text this run will install (the open world renames key items).
        var catalog = ItemCatalog.Instance;
        (byte[] Data, GameItemNames Names)? names = null;
        // Whether the game names an item at all comes from the vanilla text: nothing unnamed is ever handed out.
        var vanillaNames = new GameItemNames(new BattlePack(original.Extract(original.Find(BattlePack.UsPath)!)), catalog);
        catalog.NamedInGame = vanillaNames.IsNamed;
        catalog.GameNames = id =>
        {
            var data = context.ChangedFiles.GetValueOrDefault(BattlePack.UsPath) ?? (names?.Data ?? original.Extract(original.Find(BattlePack.UsPath)!));
            if (names?.Data != data) names = (data, new GameItemNames(new BattlePack(data), catalog));
            return names.Value.Names.NameOf(id);
        };
        try
        {
            foreach (var feature in enabled) feature.Apply(context);
        }
        finally
        {
            catalog.GameNames = null;
            catalog.NamedInGame = null;
        }
        var problems = enabled.SelectMany(f => f.Validate(context)).Distinct().ToList();
        foreach (var (name, bytes) in context.ChangedFiles)
        {
            var entry = original.Find(name);
            if (entry is null) problems.Add($"{name} is not in the game archive.");
            else if (Math.Max(1, (bytes.Length + VbfArchive.BlockSize - 1L) / VbfArchive.BlockSize) > Math.Max(1, entry.BlockCount))
                problems.Add($"{name} grew past its original size limit ({entry.BlockCount} × 64 KB).");
        }
        return new RunResult(seed, settings.Clone(), context.ChangedFiles, context.Spoiler, problems, context.LoaderFeatures.ToList());
    }
}
