using System.Text.Json.Nodes;

namespace Ff12Randomizer.Core.Storage;

/// <summary>One spoiler section as a flat table: a row per entry (or per sub-entry, e.g. per shop stage).</summary>
public sealed record SpoilerTable(string Key, string Title, IReadOnlyList<string> Columns, IReadOnlyList<string[]> Rows);

/// <summary>
/// Reads a spoiler log for the app's viewer. Works on any section without knowing its features, so new features show
/// up automatically:
/// <list type="bullet">
/// <item>Every top-level array of objects is a section.</item>
/// <item>A nested object becomes more columns. A column that would repeat a name gets its parent as a prefix, so
/// <c>was.common</c> next to <c>common</c> becomes "Was common".</item>
/// <item><c>{"was": a, "now": b}</c> is shown as "a → b".</item>
/// <item>Lists of values are joined with commas. One list of objects per entry (shop stages, board cells) gives a row
/// per item, with the entry's other columns repeated.</item>
/// </list>
/// </summary>
public sealed class SpoilerDocument
{
    public string? Seed { get; private init; }
    public DateTimeOffset? CreatedAt { get; private init; }
    public IReadOnlyList<string> Summary { get; private init; } = [];
    public IReadOnlyList<SpoilerTable> Tables { get; private init; } = [];

    public static SpoilerDocument Load(string path) => Parse(JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException("The spoiler log isn't a JSON object."));

    public static SpoilerDocument Parse(JsonObject root)
    {
        var tables = new List<SpoilerTable>();
        foreach (var (key, node) in root)
        {
            if (node is not JsonArray array || array.Count == 0 || array.Any(e => e is not JsonObject)) continue;
            var columns = new List<string>();
            var rows = new List<Dictionary<string, string>>();
            foreach (JsonObject entry in array.Cast<JsonObject>()) rows.AddRange(Rows(entry, columns));
            tables.Add(new SpoilerTable(key, Humanize(key), columns,
                rows.Select(r => columns.Select(c => r.GetValueOrDefault(c, "")).ToArray()).ToList()));
        }
        return new SpoilerDocument
        {
            Seed = root["seed"]?.ToString(),
            CreatedAt = root["createdAt"] is JsonValue v && v.TryGetValue(out string? s) && DateTimeOffset.TryParse(s, out var when) ? when : null,
            Summary = root["summary"] is JsonArray summary ? summary.Select(x => x?.ToString() ?? "").ToList() : [],
            Tables = tables,
        };
    }

    private static IEnumerable<Dictionary<string, string>> Rows(JsonObject entry, List<string> columns)
    {
        var cells = new Dictionary<string, string>();
        JsonArray? children = null;
        Flatten(entry, null, cells, columns, ref children);
        if (children is null || children.Count == 0)
        {
            yield return cells;
            yield break;
        }
        foreach (var child in children)
        {
            var row = new Dictionary<string, string>(cells);
            JsonArray? none = null;
            Flatten((JsonObject)child!, null, row, columns, ref none);
            yield return row;
        }
    }

    private static void Flatten(JsonObject obj, string? prefix, Dictionary<string, string> cells, List<string> columns,
        ref JsonArray? children)
    {
        foreach (var (key, node) in obj)
        {
            if (node is JsonObject inner && !IsChange(inner))
            {
                Flatten(inner, key, cells, columns, ref children);
                continue;
            }
            if (node is JsonArray list && list.Count > 0 && list.All(x => x is JsonObject) && children is null && prefix is null)
            {
                children = list;
                continue;
            }
            string name = Humanize(key);
            if (cells.ContainsKey(name) && prefix is not null) name = Humanize(prefix + " " + key);
            cells[name] = Text(node);
            if (!columns.Contains(name)) columns.Add(name);
        }
    }

    private static bool IsChange(JsonObject o) => o.Count == 2 && o.ContainsKey("was") && o.ContainsKey("now")
        && o["was"] is JsonValue && o["now"] is JsonValue;

    private static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonObject o when IsChange(o) => $"{Text(o["was"])} → {Text(o["now"])}",
        JsonArray a when a.All(x => x is null or JsonValue) => string.Join(", ", a.Select(Text)),
        JsonValue v => v.ToString(),
        _ => node.ToJsonString(),
    };

    /// <summary>"diamondArmlet" → "Diamond armlet", "hp" → "HP", "was common" → "Was common".</summary>
    public static string Humanize(string key)
    {
                var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (char c in key)
        {
            if (c == ' ' || c == '_' || (char.IsUpper(c) && current.Length > 0))
            {
                if (current.Length > 0) words.Add(current.ToString());
                current.Clear();
                if (c is ' ' or '_') continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) words.Add(current.ToString());
        string text = string.Join(" ", words.Select(w => IsAbbreviation(w) ? w.ToUpperInvariant() : w.ToLowerInvariant()));
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Short words with no vowel are abbreviations: hp, mp.</summary>
    private static bool IsAbbreviation(string word) => word.Length <= 3 && !word.Any(c => "aeiouyAEIOUY".Contains(c));
}
