using System.Reflection;

namespace Ff12Randomizer.Core.GameData;

public enum ItemCategory { Item, Weapon, Armor, Accessory, Ability, Loot, Key, Esper, Board, Other }

/// <summary>One item, gear piece, magick/technick, loot or key item, with its rank (rough game progress, 1 = earliest).</summary>
public sealed record ItemInfo(ushort Id, string Name, int Rank, ItemCategory Category, IReadOnlySet<string> Traits)
{
    /// <summary>Items the randomizer should never hand out or take away (cut content, story-critical...).</summary>
    public bool Ignored => Traits.Contains("Ignore");

    /// <summary>Gear, consumables, magicks/technicks and loot: what may freely move between chests and shops.</summary>
    /// <remarks>Unranked items other than loot (story pieces like the Treaty-Blade or Teleport Stone) never move.</remarks>
    public bool Shuffleable => !Ignored && (Rank > 0 || Category == ItemCategory.Loot) && Category is ItemCategory.Item
        or ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Accessory or ItemCategory.Ability or ItemCategory.Loot;

    public override string ToString() => $"{Name} ({Id:X4})";
}

/// <summary>
/// Every item the game knows, from data/items.csv (taken from Bartz24's FFModdingWPF, MIT; see third_party).
/// </summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<ushort, ItemInfo> _byId;

    private ItemCatalog(IEnumerable<ItemInfo> items) => _byId = items.ToDictionary(i => i.Id);

    public static ItemCatalog Instance { get; } = Load();

    public IEnumerable<ItemInfo> All => _byId.Values;

    public ItemInfo? Find(ushort id) => _byId.GetValueOrDefault(id);

    /// <summary>A readable name for any id, including ones not in the catalog.</summary>
    public string NameOf(ushort id) => Find(id)?.Name ?? GameNames?.Invoke(id) ?? $"Unknown {id:X4}";

    /// <summary>
    /// Names for ids items.csv lacks (guest gear, maps and other key items), from the game's own text. Set by
    /// <see cref="Randomization.RandomizerEngine"/> for the duration of a run.
    /// </summary>
    public Func<ushort, string?>? GameNames { get; set; }

    /// <summary>
    /// Whether the game itself names an item: false for things it never shows (guest-only gear, marked NOT USED), null when
    /// unknown. Set with <see cref="GameNames"/>; randomizers never hand out items the game doesn't name.
    /// </summary>
    public Func<ushort, bool?>? NamedInGame { get; set; }

    private static ItemCatalog Load()
    {
        var items = new List<ItemInfo>();
        foreach (var row in Csv.ReadEmbedded("items.csv").Skip(1))
        {
            if (row.Count < 6 || !ushort.TryParse(row[1], System.Globalization.NumberStyles.HexNumber, null, out ushort id)) continue;
            int rank = int.TryParse(row[2], out int r) ? r : 0;
            var category = Enum.TryParse<ItemCategory>(row[5], out var c) ? c : ItemCategory.Other;
            var traits = row[4].Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            items.Add(new ItemInfo(id, row[0], rank, category, traits));
        }
        return new ItemCatalog(items);
    }
}

/// <summary>Minimal CSV reading (quoted fields may hold commas) for the data files embedded in Core.</summary>
internal static class Csv
{
    public static IEnumerable<IReadOnlyList<string>> ReadEmbedded(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"data/{fileName}")
            ?? throw new InvalidOperationException($"data/{fileName} is missing from this build.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0) yield return ParseLine(line);
        }
    }

    public static IReadOnlyList<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else current.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(ch);
        }
        fields.Add(current.ToString());
        return fields;
    }
}

/// <summary>
/// Item names from the game's battle text (battle_pack section 61). Each kind of item (the high 4 bits of its id: 0x1xxx
/// equipment, 0x2xxx loot, 0x8xxx key items...) is named by one text table at index id &amp; 0xFFF. Which table is found
/// from the data: the one whose entries agree with the most names items.csv already has for that kind.
/// </summary>
public sealed class GameItemNames
{
    private const int MaxTables = 64;
    private static readonly string[] Placeholders = ["NOT USED", "J6dRX", "dummy"];
    private readonly BattlePack _pack;
    private readonly Dictionary<int, int> _tableByKind = [];

    public GameItemNames(BattlePack pack, ItemCatalog catalog)
    {
        _pack = pack;
        foreach (var kind in catalog.All.GroupBy(i => i.Id >> 12))
        {
            int best = -1, bestMatches = 0;
            for (int t = 0; t < MaxTables; t++)
            {
                int matches;
                try { matches = kind.Count(i => pack.Text(t, i.Id & 0xFFF) == i.Name); }
                catch (ArgumentOutOfRangeException) { break; }
                if (matches > bestMatches) (best, bestMatches) = (t, matches);
            }
            // Only trust a table that names most of what the catalog knows of this kind.
            if (best >= 0 && bestMatches * 2 >= kind.Count()) _tableByKind[kind.Key] = best;
        }
    }

    /// <summary>True when the game names <paramref name="id"/>, false for a placeholder or missing entry, null when no
    /// text table is known for its kind.</summary>
    public bool? IsNamed(ushort id)
    {
        if (!_tableByKind.TryGetValue(id >> 12, out int table)) return null;
        string? text = _pack.Text(table, id & 0xFFF);
        return text is not null && !Placeholders.Any(p => text.StartsWith(p, StringComparison.Ordinal));
    }

    public string? NameOf(ushort id)
    {
        if (!_tableByKind.TryGetValue(id >> 12, out int table)) return null;
        string? text = _pack.Text(table, id & 0xFFF);
        if (text is null) return null;
        // Things the game never shows (guest-only gear) are marked "NOT USED" and the like in all of its text.
        if (!Placeholders.Any(p => text.StartsWith(p, StringComparison.Ordinal))) return text;
        return KindOf(id) is { } kind ? $"{kind} type, no name ({id:X4})" : $"No name ({id:X4})";
    }

    private Dictionary<byte, string>? _kinds;

    /// <summary>
    /// What kind of equipment an id is, named after the licenses that cover equipment of the same class (equipment record
    /// type, byte 0x09 of the equipment record): "Shields", "Swords"... The license name without its trailing number.
    /// </summary>
    private string? KindOf(ushort id)
    {
        const int Equipment = 13, Licenses = 12, LicenseNames = 3, First = 0x1000;
        var gear = _pack.Table(Equipment);
        byte? ClassOf(int item)
        {
            int i = item - First;
            return i < 0 || i >= gear.Count ? null : gear.Record(i)[0x09];
        }
        if (_kinds is null)
        {
            _kinds = [];
            var licenses = _pack.Table(Licenses);
            for (int l = 0; l < licenses.Count; l++)
            {
                string? name = _pack.Text(LicenseNames, l)?.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ');
                if (string.IsNullOrEmpty(name)) continue;
                var record = licenses.Record(l);
                for (int s = 0; s < 8; s++)
                {
                    int item = record[0x08 + s * 2] | record[0x09 + s * 2] << 8;
                    if (ClassOf(item) is { } c) _kinds.TryAdd(c, name);
                }
            }
        }
        return ClassOf(id) is { } cls ? _kinds.GetValueOrDefault(cls) : null;
    }
}
