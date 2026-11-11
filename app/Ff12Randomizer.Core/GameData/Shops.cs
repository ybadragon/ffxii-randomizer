using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>
/// battle_pack.bin section 39: the shops' stock lists. A tree of nodes, each with a 0xC byte header (u32 tag, u16 element
/// size, u16 count, u32 data offset; offsets relative to the section): the root lists the shops (tag 0x64), a shop lists
/// its stock per story stage (tag 0x164), and a stage holds u16 item ids (tag 0x264). Layout from Bartz24's FFModdingWPF
/// (MIT). Items are only replaced in place, so the section never changes size. Where each list is sold is found from the
/// map scripts by <see cref="ShopLocations"/>.
/// </summary>
public sealed class ShopTable
{
    public const int Section = 39;
    private const uint RootTag = 0x64, ShopTag = 0x164, ListTag = 0x264;

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
            shops.Add(new ShopStock(i, rootId, lists));
        }
        Shops = shops;
    }

    public IReadOnlyList<ShopStock> Shops { get; }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_base + offset));
    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_base + offset));

    public sealed record ShopStock(int Index, uint RootId, IReadOnlyList<StockList> Lists);

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
