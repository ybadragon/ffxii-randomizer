using System.Text.Json;
using System.Text.Json.Nodes;
using Ff12Randomizer.Core.Vbf;

namespace Ff12Randomizer.Core.Randomization;

/// <summary>What a feature works with: the original game files, the settings, its own random stream and the spoiler log.</summary>
public sealed class RandomizerContext(VbfArchive original, RandomizerSettings settings, Seed seed)
{
    private static readonly JsonSerializerOptions SpoilerJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, byte[]> _changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string), JsonObject> _entries = [];

    public VbfArchive Original { get; } = original;
    public RandomizerSettings Settings { get; } = settings;
    public Seed Seed { get; } = seed;

    public IReadOnlyDictionary<string, byte[]> ChangedFiles => _changed;

    /// <summary>Extra behaviour the loader DLL should switch on, each written as an empty NAME.on file in the ff12rando folder (e.g. hunt_rewards.on).</summary>
    public ISet<string> LoaderFeatures { get; } = new SortedSet<string>(StringComparer.Ordinal);

    /// <summary>Structured spoiler data by section ("party", "treasure", ...); each section is a list of entries.</summary>
    public JsonObject Spoiler { get; } = new();

    public Rng RngFor(string section) => Rng.For(Seed, section);

    /// <summary>
    /// The working copy of a game file. The first call reads the original from the archive; later calls
    /// (from any feature) return the same copy, so edits accumulate. Every file fetched here is written to the game.
    /// </summary>
    public byte[] Edit(string archivePath)
    {
        if (!_changed.TryGetValue(archivePath, out byte[]? bytes))
        {
            var entry = Original.Find(archivePath) ?? throw new FileNotFoundException($"{archivePath} is not in the game archive.");
            bytes = Original.Extract(entry);
            _changed[archivePath] = bytes;
        }
        return bytes;
    }

    /// <summary>
    /// Replaces the working copy of a game file, for edits that change its length. The loader only accepts files that
    /// keep within their original count of 64 KB blocks.
    /// </summary>
    public void Replace(string archivePath, byte[] bytes) => _changed[archivePath] = bytes;

    /// <summary>Adds one entry (any serializable object, camelCased) to a spoiler section.</summary>
    public void Spoil(string section, object entry) => SectionList(section).Add(ToNode(entry));

    /// <summary>
    /// The spoiler entry for one thing (e.g. one enemy), shared by every feature that changes it, so each adds its own
    /// properties to the same object. <paramref name="identity"/> seeds a new entry's first properties.
    /// </summary>
    public JsonObject SpoilerEntry(string section, string key, object identity)
    {
        if (_entries.TryGetValue((section, key), out var entry)) return entry;
        entry = (JsonObject)ToNode(identity)!;
        SectionList(section).Add(entry);
        _entries[(section, key)] = entry;
        return entry;
    }

    public static JsonNode? ToNode(object value) => JsonSerializer.SerializeToNode(value, SpoilerJson);

    private JsonArray SectionList(string section)
    {
        if (Spoiler[section] is not JsonArray list) Spoiler[section] = list = [];
        return list;
    }
}

public interface IRandomizerFeature
{
    bool IsEnabled(RandomizerSettings settings);
    void Apply(RandomizerContext context);

    /// <summary>
    /// Checks the finished run for rule breaks this feature must never produce. Any message returned stops the run
    /// before the game is touched.
    /// </summary>
    IEnumerable<string> Validate(RandomizerContext context) => [];
}
