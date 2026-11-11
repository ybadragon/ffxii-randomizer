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

    /// <summary>The bytes of one section (up to the next section's start, or the end of the file).</summary>
    public byte[] Section(int section) => Section(Data, section);

    private static byte[] Section(byte[] data, int section)
    {
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        int start = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + section * 4));
        int end = section + 1 < count ? (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8 + section * 4)) : data.Length;
        return data.AsSpan(start, end - start).ToArray();
    }

    /// <summary>
    /// A copy of a packed file (battle_pack.bin, or section 61 which has the same layout: u32 count, u32 offsets, then the
    /// parts) with some parts replaced. Parts start on <paramref name="alignment"/> byte boundaries (8 in battle_pack.bin,
    /// 4 in section 61, as in the originals), and the offset table is
    /// rebuilt, so replacements may change size.
    /// </summary>
    public static byte[] Repack(byte[] data, IReadOnlyDictionary<int, byte[]> replacements, int alignment = 8)
    {
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        int first = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var output = new MemoryStream();
        output.Write(data.AsSpan(0, first)); // count, offsets (rewritten below) and any padding before the first part
        var offsets = new int[count];
        for (int s = 0; s < count; s++)
        {
            while (output.Length % alignment != 0) output.WriteByte(0);
            offsets[s] = (int)output.Length;
            output.Write(replacements.TryGetValue(s, out var part) ? part : Section(data, s));
        }
        var result = output.ToArray();
        for (int s = 0; s < count; s++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + s * 4), (uint)offsets[s]);
        return result;
    }

    /// <summary>Section 61: the battle text tables (0 abilities, 1 equipment, 8 character and enemy names...).</summary>
    public const int TextSection = 61, NamesTable = 8;

    /// <summary>
    /// String <paramref name="index"/> of text table <paramref name="table"/> in section 61: a u32 table count and u32 table
    /// offsets, each table a u32 string count and u32 string offsets, relative to their own start; strings begin with two
    /// bytes before the text.
    /// </summary>
    public string? Text(int table, int index)
    {
        int section = SectionOffset(TextSection);
        if (table < 0 || table >= (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(section))) return null;
        int sub = section + (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(section + 4 + table * 4));
        if (index < 0 || index >= (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(sub))) return null;
        int start = sub + (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(sub + 4 + index * 4)) + 2;
        string text = start < Data.Length ? GameText.Decode(Data.AsSpan(start)) : "";
        return text.Length == 0 ? null : text;
    }
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
