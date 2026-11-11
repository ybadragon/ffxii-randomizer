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
    public string NameOf(ushort id) => Find(id)?.Name ?? $"Unknown {id:X4}";

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
