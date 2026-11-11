namespace Ff12Randomizer.Core.Storage;

/// <summary>Where the app keeps its own files (not the game's).</summary>
public sealed class AppPaths(string root)
{
    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXII Randomizer"));

    public string Root { get; } = root;
    public string StatePath => Path.Combine(Root, "state.json");
    public string PresetsPath => Path.Combine(Root, "presets.json");
    public string HistoryPath => Path.Combine(Root, "history.json");
    public string SpoilerDirectory => Path.Combine(Root, "spoilers");
}
