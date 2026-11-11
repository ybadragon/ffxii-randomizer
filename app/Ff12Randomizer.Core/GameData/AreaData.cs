using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>
/// An area's enemy data (ps2data/plan_master/in/plan_map/&lt;area&gt;/area/&lt;area&gt;.ard). Four tables, each found through
/// a u32 pointer in the file header, with the record count at pointer + 4 and records from pointer + 0x20:
/// extended info (0x54 bytes, pointer at 0x10), basic info (0x58, at 0x18), default stats (0x38, at 0x24) and level stats
/// (0x38, at 0x28). Layout from Bartz24's FFModdingWPF (MIT). Records are edited in place.
/// </summary>
public sealed class AreaData
{
    public const int ExtendedSize = 0x54, BasicSize = 0x58, StatsSize = 0x38;

    public AreaData(string archivePath, byte[] data)
    {
        ArchivePath = archivePath;
        Data = data;
        Extended = new Table(data, Pointer(0x10), ExtendedSize);
        Basic = new Table(data, Pointer(0x18), BasicSize);
        DefaultStats = new Table(data, Pointer(0x24), StatsSize);
        LevelStats = new Table(data, Pointer(0x28), StatsSize);
    }

    public string ArchivePath { get; }
    public string AreaId => Path.GetFileNameWithoutExtension(ArchivePath);
    public byte[] Data { get; }
    public Table Extended { get; }
    public Table Basic { get; }
    public Table DefaultStats { get; }
    public Table LevelStats { get; }

    private int Pointer(int at) => (int)BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(at));

    public static bool IsAreaFile(string name) =>
        name.StartsWith("ps2data/plan_master/in/plan_map/") && name.EndsWith(".ard") && name.Contains("/area/");

    public readonly struct Table(byte[] data, int pointer, int recordSize)
    {
        public int Count => (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pointer + 4));
        public int RecordSize => recordSize;
        public Memory<byte> this[int index] => data.AsMemory(pointer + 0x20 + index * recordSize, recordSize);
    }
}
