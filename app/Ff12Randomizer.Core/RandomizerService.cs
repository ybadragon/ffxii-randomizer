using Ff12Randomizer.Core.Game;
using Ff12Randomizer.Core.Patching;
using Ff12Randomizer.Core.Randomization;
using Ff12Randomizer.Core.Storage;

namespace Ff12Randomizer.Core;

public enum RunStep { CheckingGame, ReadingGameData, Randomizing, WritingAndChecking, Saving }

public sealed record RunOutcome(HistoryEntry Entry, RunResult Result);

/// <summary>This version of the randomizer makes a different game for a History run, so its spoiler log can't be rebuilt.</summary>
public sealed class SpoilerMismatchException(string seed)
    : Exception($"This version of the randomizer makes a slightly different game for seed {seed}, so a new spoiler log wouldn't match the game that run made.");

/// <summary>One full run: randomize from the original data, install it into the game, then record it in history.</summary>
/// <remarks>The app passes its own stores so a run appears in its History and preset lists straight away. Each store
/// caches what it loaded, so separate instances would not see each other's changes until a restart.</remarks>
public sealed class RandomizerService(GameInstall game, AppPaths paths, PresetStore? presets = null, HistoryStore? history = null)
{
    public GameInstall Game { get; } = game;
    public PatchManager Patcher { get; } = new(game);
    public PresetStore Presets { get; } = presets ?? new(paths);
    public HistoryStore History { get; } = history ?? new(paths);

    public RunOutcome Run(RandomizerSettings settings, Seed seed, Preset? preset, IProgress<RunStep>? progress = null)
    {
        progress?.Report(RunStep.CheckingGame);
        if (GameInstall.IsGameRunning()) throw new GameRunningException();
        if (Patcher.GetStatus() == GameDataStatus.OtherLoader) throw new OtherLoaderException(Game.LoaderPath);

        progress?.Report(RunStep.ReadingGameData);
        var original = Patcher.OpenOriginal();

        progress?.Report(RunStep.Randomizing);
        var result = RandomizerEngine.Run(original, settings, seed);
        if (result.Problems.Count > 0) throw new ValidationFailedException(result.Problems);
        if (result.Files.Count == 0)
            throw new InvalidOperationException("None of the selected options change the game yet. Turn on an option that's available.");

        progress?.Report(RunStep.WritingAndChecking);
        string fingerprint = Patcher.Apply(original, result.Files, result.LoaderFeatures);

        progress?.Report(RunStep.Saving);
        var entry = new HistoryEntry
        {
            Seed = seed.Text,
            Settings = settings.Clone(),
            PresetId = preset?.Id,
            PresetName = preset?.Name,
            FileCount = result.Files.Count,
            PatchedFingerprint = fingerprint,
        };
        if (settings.IsOn("spoiler")) entry.SpoilerPath = SpoilerLog.Write(paths, result, entry);
        History.Add(entry);
        if (preset is not null) Presets.MarkUsed(preset.Id);
        return new RunOutcome(entry, result);
    }

    /// <summary>
    /// Rewrites a History run's spoiler log with this version's labels and sections. It first rebuilds the run and checks
    /// the result is byte for byte the game that run installed, so the log can never describe a different game. Doesn't
    /// touch the game. The previous log is kept next to it as .bak.
    /// </summary>
    /// <exception cref="SpoilerMismatchException">This version makes a different game for that run's seed and settings.</exception>
    public string RefreshSpoiler(HistoryEntry entry)
    {
        var original = Patcher.OpenOriginal();
        var result = RandomizerEngine.Run(original, entry.Settings, new Seed(entry.Seed));
        if (entry.PatchedFingerprint is null || PatchManager.FingerprintOf(original, result.Files) != entry.PatchedFingerprint)
            throw new SpoilerMismatchException(entry.Seed);
        if (entry.SpoilerPath is { } old && File.Exists(old)) File.Copy(old, old + ".bak", overwrite: true);
        entry.SpoilerPath = SpoilerLog.Write(paths, result, entry);
        History.Update(entry);
        return entry.SpoilerPath;
    }

    /// <summary>The history entry whose run is in the game right now, if any.</summary>
    public HistoryEntry? AppliedEntry()
    {
        if (Patcher.GetStatus() != GameDataStatus.Randomized) return null;
        string? fingerprint = Patcher.CurrentFingerprint();
        return History.Entries.FirstOrDefault(e => e.PatchedFingerprint == fingerprint);
    }
}
