using System.IO.Compression;
using System.Text;

namespace Ff12Randomizer.Core.Vbf;

/// <summary>One file inside FFXII_TZA.vbf.</summary>
public sealed record VbfEntry(
    int Index,
    uint BlockStart,
    uint Flags,
    ulong Size,
    ulong Offset,
    ulong NameOffset,
    string Name)
{
    public long BlockCount => (long)((Size + VbfArchive.BlockSize - 1) / VbfArchive.BlockSize);
}

/// <summary>
/// Reader for the game's single data archive.
///
/// Layout (all little endian):
///   0x00  "SRYK"
///   0x04  u32  header length (the first file's data starts here)
///   0x08  u64  file count N
///   0x10  N x 16 byte MD5 of each file name
///         N x 32 byte entries: u32 blockStart, u32 flags, u64 size, u64 dataOffset, u64 nameOffset
///         u32 name table length, then NUL terminated names
///         u16 per 64 KiB block: compressed length, 0 meaning a stored (uncompressed) 64 KiB block
///   Compressed blocks are zlib streams.
/// </summary>
public sealed class VbfArchive
{
    public const int BlockSize = 0x10000;
    public const uint Magic = 0x4B595253; // "SRYK"

    public string Path { get; }
    public byte[] Header { get; }
    public IReadOnlyList<VbfEntry> Entries { get; }
    public int HashTableOffset => 16;
    public int EntryTableOffset { get; }
    public int NameTableOffset { get; }
    public int BlockTableOffset { get; }

    private readonly Dictionary<string, VbfEntry> byName;

    private readonly Func<Stream> open;

    /// <param name="open">Opens the archive bytes; defaults to the file itself. Lets a patched (virtual) archive be read the same way.</param>
    public VbfArchive(string path, Func<Stream>? open = null)
    {
        Path = path;
        this.open = open ?? (() => File.OpenRead(path));
        using var fs = this.open();
        var head = new byte[16];
        fs.ReadExactly(head);
        if (BitConverter.ToUInt32(head, 0) != Magic)
        {
            throw new InvalidDataException($"{path} is not a VBF archive.");
        }

        Header = new byte[BitConverter.ToUInt32(head, 4)];
        fs.Position = 0;
        fs.ReadExactly(Header);

        int count = checked((int)BitConverter.ToUInt64(Header, 8));
        EntryTableOffset = 16 + count * 16;
        NameTableOffset = EntryTableOffset + count * 32;
        // The name table length counts its own 4 byte length field.
        BlockTableOffset = NameTableOffset + (int)BitConverter.ToUInt32(Header, NameTableOffset);

        var entries = new VbfEntry[count];
        int names = NameTableOffset + 4;
        for (int i = 0; i < count; i++)
        {
            var s = Header.AsSpan(EntryTableOffset + i * 32, 32);
            ulong nameOffset = BitConverter.ToUInt64(s[24..]);
            var nameSpan = Header.AsSpan(names + (int)nameOffset);
            string name = Encoding.ASCII.GetString(nameSpan[..nameSpan.IndexOf((byte)0)]);
            entries[i] = new VbfEntry(i,
                BitConverter.ToUInt32(s),
                BitConverter.ToUInt32(s[4..]),
                BitConverter.ToUInt64(s[8..]),
                BitConverter.ToUInt64(s[16..]),
                nameOffset,
                name);
        }

        Entries = entries;
        byName = entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The archive ends with the MD5 of its header; the game refuses to start when they disagree.</summary>
    public bool HeaderChecksumValid()
    {
        using var fs = open();
        var trailer = new byte[16];
        fs.Seek(-16, SeekOrigin.End);
        fs.ReadExactly(trailer);
        return System.Security.Cryptography.MD5.HashData(Header).AsSpan().SequenceEqual(trailer);
    }

    public VbfEntry? Find(string name) => byName.GetValueOrDefault(name.Replace('\\', '/'));

    public int CompressedBlockLength(long blockIndex) =>
        BitConverter.ToUInt16(Header, BlockTableOffset + checked((int)(blockIndex * 2)));

    public byte[] Extract(VbfEntry entry)
    {
        var output = new byte[entry.Size];
        using var fs = open();
        fs.Position = (long)entry.Offset;
        for (long b = 0; b < entry.BlockCount; b++)
        {
            int want = (int)Math.Min(BlockSize, (long)entry.Size - b * BlockSize);
            int stored = CompressedBlockLength(entry.BlockStart + b);
            int length = stored == 0 ? BlockSize : stored;
            var buf = new byte[length];
            fs.ReadExactly(buf);
            var dest = output.AsSpan((int)(b * BlockSize), want);

            // A block that did not shrink is stored as is: 0 for a full block, or the
            // exact remaining length for a short final block.
            if (stored == 0 || length == want)
            {
                buf.AsSpan(0, want).CopyTo(dest);
                continue;
            }

            using var z = new ZLibStream(new MemoryStream(buf), CompressionMode.Decompress);
            z.ReadExactly(dest);
        }

        return output;
    }
}
