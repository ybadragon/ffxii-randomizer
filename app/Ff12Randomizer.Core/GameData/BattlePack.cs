using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>
/// battle_pack.bin: most of the battle data (items, gear, party, enemies' rewards...). It starts with a u32 section count
/// and a u32 offset per section. Many sections are tables with a 0x20 byte header: u16 count at 0x4, u16 record size at
/// 0x8 and u16 offset of the first record at 0xC (layout as in Bartz24's FFModdingWPF, MIT).
/// </summary>
public sealed class BattlePack(byte[] data)
{
    public const string UsPath = "ps2data/image/ff12/test_battle/us/binaryfile/battle_pack.bin";

    /// <summary>Party members' starting data, one 0x80 byte record each.</summary>
    public const int PartySection = 16;

    public byte[] Data { get; } = data;

    public int SectionCount => (int)BinaryPrimitives.ReadUInt32LittleEndian(Data);

    public int SectionOffset(int section)
    {
        if (section < 0 || section >= SectionCount) throw new ArgumentOutOfRangeException(nameof(section));
        return (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(4 + section * 4));
    }

    /// <summary>A table section's records, as writable slices of <see cref="Data"/>.</summary>
    public TableSection Table(int section) => new(Data, SectionOffset(section));
}

public readonly struct TableSection(byte[] data, int offset)
{
    public int Count => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 0x4));
    public int RecordSize => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 0x8));
    private int FirstRecord => offset + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 0xC));

    public Span<byte> Record(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return data.AsSpan(FirstRecord + index * RecordSize, RecordSize);
    }
}

/// <summary>Starting data of one party member (0x80 bytes): up to 10 starting items as ids at 0x58 and amounts at 0x34.</summary>
public ref struct PartyMemberRecord(Span<byte> record)
{
    public const int ItemSlots = 10;
    public const ushort NoItem = 0xFFFF;

    private readonly Span<byte> _record = record;

    public ushort ItemId(int slot) => BinaryPrimitives.ReadUInt16LittleEndian(_record[(0x58 + slot * 2)..]);
    public byte ItemAmount(int slot) => _record[0x34 + slot];
    public void SetItemAmount(int slot, byte amount) => _record[0x34 + slot] = amount;
}
