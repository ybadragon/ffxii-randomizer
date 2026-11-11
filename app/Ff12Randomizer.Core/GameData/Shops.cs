using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>A shop from data/shops.csv (Bartz24's FFModdingWPF, MIT). <see cref="Id"/> is its index in the shop table.</summary>
public sealed record ShopInfo(string Name, int Id, string Location, IReadOnlySet<string> Traits);

/// <summary>
/// battle_pack.bin section 39: the shops. A tree of nodes, each with a 0xC byte header (u32 tag, u16 element size,
/// u16 count, u32 data offset; offsets relative to the section): the root lists shops (tag 0x64), a shop lists its stock
/// lists (tag 0x164, one per story stage), and a stock list holds u16 item ids (tag 0x264).
/// Layout from Bartz24's FFModdingWPF (MIT). Items are only replaced in place, so the section never changes size.
/// </summary>
public sealed class ShopTable
{
    public const int Section = 39;
    private const uint RootTag = 0x64, ShopTag = 0x164, ListTag = 0x264;

    /// <summary>
    /// Shop names confirmed in game where data/shops.csv disagrees. The CSV id is the shop's position in the table, which
    /// holds for the shops checked so far, but a few lesser-visited ones are labelled wrong.
    /// </summary>
    private static readonly Dictionary<int, (string Name, string Location)> Verified = new()
    {
        // 2026-11-10: this stock list (Bacchus's Wine, Remedy, ... over 9 stages) is what the Stranded Merchant in
        // Old Archades (Alley of Low Whispers) sells; the CSV calls it Tetran on the Dreadnought Leviathan.
        [25] = ("Stranded Merchant", "Old Archades"),
    };

    public static IReadOnlyList<ShopInfo> Known { get; } = Csv.ReadEmbedded("shops.csv").Skip(1)
        .Where(r => r.Count >= 4 && int.TryParse(r[1], out _))
        .Select(r => new ShopInfo(r[0], int.Parse(r[1]), r[2], r[3].Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)))
        .Select(s => Verified.TryGetValue(s.Id, out var v) ? s with { Name = v.Name, Location = v.Location }
            // The CSV's other "Stranded Merchant" row (36: a Teleport Stone and Spoiled Gysahl Greens) is unconfirmed.
            : s.Id == 36 ? s with { Name = "Unidentified shop (Teleport Stone)", Location = "" } : s)
        .ToList();

    private readonly byte[] _data;
    private readonly int _base;

    public ShopTable(BattlePack pack)
    {
        _data = pack.Data;
        _base = pack.SectionOffset(Section);
        if (U32(0) != RootTag) throw new InvalidDataException("battle_pack.bin section 39 isn't the shop table.");

        int shopCount = U16(0x6), rootStart = (int)U32(0x8);
        var shops = new List<ShopStock>();
        for (int i = 0; i < shopCount; i++)
        {
            uint rootId = U32(rootStart + i * 8);
            int shopNode = (int)U32(rootStart + i * 8 + 4);
            if (U32(shopNode) != ShopTag) throw new InvalidDataException($"Shop {i} has an unexpected node tag.");
            int listCount = U16(shopNode + 0x6), listStart = (int)U32(shopNode + 0x8);
            var lists = new List<StockList>();
            for (int j = 0; j < listCount; j++)
            {
                int listNode = (int)U32(listStart + j * 8 + 4);
                if (U32(listNode) != ListTag) throw new InvalidDataException($"Shop {i} list {j} has an unexpected node tag.");
                lists.Add(new StockList(this, _base + (int)U32(listNode + 0x8), U16(listNode + 0x6)));
            }
            shops.Add(new ShopStock(i, rootId, Known.FirstOrDefault(s => s.Id == i), lists));
        }
        Shops = shops;
    }

    public IReadOnlyList<ShopStock> Shops { get; }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_base + offset));
    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_base + offset));

    public sealed record ShopStock(int Index, uint RootId, ShopInfo? Info, IReadOnlyList<StockList> Lists)
    {
        public string Name => Info?.Name ?? $"Shop {Index}";
    }

    /// <summary>One stage of a shop's stock. Entries of 0xFFFF are empty.</summary>
    public sealed class StockList(ShopTable owner, int absoluteStart, int count)
    {
        public int Count => count;

        public ushort this[int index]
        {
            get => BinaryPrimitives.ReadUInt16LittleEndian(owner._data.AsSpan(absoluteStart + index * 2));
            set => BinaryPrimitives.WriteUInt16LittleEndian(owner._data.AsSpan(absoluteStart + index * 2), value);
        }

        public IEnumerable<ushort> Items => Enumerable.Range(0, count).Select(i => this[i]);
    }
}
