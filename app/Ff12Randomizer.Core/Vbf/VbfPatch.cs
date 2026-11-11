using System.Buffers.Binary;
using System.IO.Compression;

namespace Ff12Randomizer.Core.Vbf;

/// <summary>
/// The fixed header at the start of patch.f12p. Must stay in sync with PatchHeader in loader.cpp.
/// </summary>
public sealed record VbfPatchHeader(
    ulong RealHeaderLength,
    ulong VirtualHeaderLength,
    ulong RealFileSize,
    ulong AppendedVirtualOffset,
    ulong AppendedLength,
    ulong HeaderBlobPos,
    ulong AppendedBlobPos)
{
    public const int Size = 64;
    public const uint Version = 1;
    public static ReadOnlySpan<byte> Magic => "F12P"u8;

    public ulong Delta => VirtualHeaderLength - RealHeaderLength;
    public ulong VirtualFileSize => AppendedVirtualOffset + AppendedLength;

    public void Write(Span<byte> s)
    {
        Magic.CopyTo(s);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], Version);
        BinaryPrimitives.WriteUInt64LittleEndian(s[8..], RealHeaderLength);
        BinaryPrimitives.WriteUInt64LittleEndian(s[16..], VirtualHeaderLength);
        BinaryPrimitives.WriteUInt64LittleEndian(s[24..], RealFileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(s[32..], AppendedVirtualOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(s[40..], AppendedLength);
        BinaryPrimitives.WriteUInt64LittleEndian(s[48..], HeaderBlobPos);
        BinaryPrimitives.WriteUInt64LittleEndian(s[56..], AppendedBlobPos);
    }

    public static VbfPatchHeader Read(ReadOnlySpan<byte> s)
    {
        if (!s[..4].SequenceEqual(Magic) || BinaryPrimitives.ReadUInt32LittleEndian(s[4..]) != Version)
        {
            throw new InvalidDataException("Not a version 1 F12P patch.");
        }

        return new VbfPatchHeader(
            BinaryPrimitives.ReadUInt64LittleEndian(s[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(s[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(s[24..]),
            BinaryPrimitives.ReadUInt64LittleEndian(s[32..]),
            BinaryPrimitives.ReadUInt64LittleEndian(s[40..]),
            BinaryPrimitives.ReadUInt64LittleEndian(s[48..]),
            BinaryPrimitives.ReadUInt64LittleEndian(s[56..]));
    }
}

/// <summary>
/// Builds patch.f12p: a rewritten archive header plus the compressed data of every replaced file.
/// The loader DLL stitches these together with the untouched archive at read time.
///
/// Replaced files get fresh blocks appended to the block table and their data placed after the end
/// of the real archive. Growing the block table grows the header, so every original entry's data
/// offset is shifted by the same amount (delta) and the loader subtracts it again when reading.
/// </summary>
public static class VbfPatchBuilder
{
    /// <param name="relocate">Diagnostic: files moved into the patch region using their original
    /// compressed bytes, which isolates the header rewrite from our own compression.</param>
    public static VbfPatchHeader Build(VbfArchive archive, IReadOnlyDictionary<string, byte[]> replacements, string patchPath,
        IEnumerable<string>? relocate = null)
    {
        var replaced = replacements.ToDictionary(
            kv => archive.Find(kv.Key) ?? throw new FileNotFoundException($"{kv.Key} is not in the archive."),
            kv => kv.Value);

        // Compress every replacement up front so the header size is known.
        var newBlockLengths = new List<ushort>();
        var appended = new MemoryStream();
        var placements = new Dictionary<VbfEntry, (uint BlockStart, ulong DataPos, ulong Size)>();
        uint realBlockCount = (uint)((archive.Header.Length - archive.BlockTableOffset) / 2);

        // The game rejects a header that grows or block indices past the original table (tested
        // in game), so a replaced file reuses its own block-table slots and may use at most as many
        // 64 KiB blocks as the original.
        var slotWrites = new List<(long Index, ushort Length)>();

        foreach (var (entry, data) in replaced.OrderBy(kv => kv.Key.Index))
        {
            long blocksNeeded = Math.Max(1, ((long)data.Length + VbfArchive.BlockSize - 1) / VbfArchive.BlockSize);
            if (blocksNeeded > Math.Max(1, entry.BlockCount))
            {
                throw new InvalidOperationException(
                    $"{entry.Name} grew from {entry.BlockCount} to {blocksNeeded} blocks; growing past the original block count is not supported yet.");
            }

            uint blockStart = entry.BlockStart;
            ulong dataPos = (ulong)appended.Length;
            int slotsBefore = newBlockLengths.Count;
            for (int offset = 0; offset < data.Length || offset == 0; offset += VbfArchive.BlockSize)
            {
                int length = Math.Min(VbfArchive.BlockSize, data.Length - offset);
                var block = data.AsSpan(offset, length);
                byte[] compressed = Compress(block);

                // Mirror the game's convention: a block that does not shrink is stored raw, as
                // length 0 for a full block or the exact length for a short final block.
                if (compressed.Length >= length || compressed.Length >= ushort.MaxValue)
                {
                    appended.Write(block);
                    newBlockLengths.Add(length == VbfArchive.BlockSize ? (ushort)0 : (ushort)length);
                }
                else
                {
                    appended.Write(compressed);
                    newBlockLengths.Add((ushort)compressed.Length);
                }

                if (length == 0)
                {
                    break;
                }
            }

            for (int i = slotsBefore; i < newBlockLengths.Count; i++)
            {
                slotWrites.Add((blockStart + (i - slotsBefore), newBlockLengths[i]));
            }
            newBlockLengths.RemoveRange(slotsBefore, newBlockLengths.Count - slotsBefore);
            placements[entry] = (blockStart, dataPos, (ulong)data.Length);
        }

        foreach (var name in relocate ?? [])
        {
            // Keeps the file's own block-table slots: only its data offset moves.
            var entry = archive.Find(name) ?? throw new FileNotFoundException(name);
            uint blockStart = entry.BlockStart;
            ulong dataPos = (ulong)appended.Length;
            long compressedTotal = 0;
            for (long b = 0; b < entry.BlockCount; b++)
            {
                int stored = archive.CompressedBlockLength(entry.BlockStart + b);
                compressedTotal += stored == 0 ? VbfArchive.BlockSize : stored;
            }

            using var fs = File.OpenRead(archive.Path);
            fs.Position = (long)entry.Offset;
            var raw = new byte[compressedTotal];
            fs.ReadExactly(raw);
            appended.Write(raw);
            placements[entry] = (blockStart, dataPos, entry.Size);
        }

        ulong realHeaderLength = (ulong)archive.Header.Length;
        ulong virtualHeaderLength = newBlockLengths.Count == 0
            ? realHeaderLength
            : Align16(realHeaderLength + (ulong)newBlockLengths.Count * 2);
        ulong delta = virtualHeaderLength - realHeaderLength;
        ulong realFileSize = (ulong)new FileInfo(archive.Path).Length;
        ulong appendedVirtualOffset = Align16(realFileSize + delta);

        var header = new byte[virtualHeaderLength];
        archive.Header.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)virtualHeaderLength));

        foreach (var entry in archive.Entries)
        {
            var e = header.AsSpan(archive.EntryTableOffset + entry.Index * 32, 32);
            if (placements.TryGetValue(entry, out var p))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(e, p.BlockStart);
                BinaryPrimitives.WriteUInt32LittleEndian(e[4..], p.BlockStart);
                BinaryPrimitives.WriteUInt64LittleEndian(e[8..], p.Size);
                BinaryPrimitives.WriteUInt64LittleEndian(e[16..], appendedVirtualOffset + p.DataPos);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(e[16..], entry.Offset + delta);
            }
        }

        for (int i = 0; i < newBlockLengths.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan((int)realHeaderLength + i * 2), newBlockLengths[i]);
        }

        foreach (var (index, length) in slotWrites)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(archive.BlockTableOffset + (int)index * 2), length);
        }

        // The archive ends with the MD5 of its header, which the game checks at startup.
        appended.Write(System.Security.Cryptography.MD5.HashData(header));

        var result = new VbfPatchHeader(
            realHeaderLength,
            virtualHeaderLength,
            realFileSize,
            appendedVirtualOffset,
            (ulong)appended.Length,
            HeaderBlobPos: VbfPatchHeader.Size,
            AppendedBlobPos: VbfPatchHeader.Size + virtualHeaderLength);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(patchPath))!);
        string temp = patchPath + ".tmp";
        using (var fs = File.Create(temp))
        {
            var fixedHeader = new byte[VbfPatchHeader.Size];
            result.Write(fixedHeader);
            fs.Write(fixedHeader);
            fs.Write(header);
            appended.Position = 0;
            appended.CopyTo(fs);
        }

        File.Move(temp, patchPath, overwrite: true);
        return result;
    }

    private static byte[] Compress(ReadOnlySpan<byte> block)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(block);
        }

        return ms.ToArray();
    }

    private static ulong Align16(ulong value) => (value + 15) & ~15UL;
}

/// <summary>
/// The archive as the game sees it through the loader. Mirrors ReadVirtual in loader.cpp so the
/// patch can be checked without launching the game.
/// </summary>
public sealed class VirtualVbfStream : Stream
{
    private readonly FileStream real;
    private readonly FileStream patchFile;
    private readonly VbfPatchHeader patch;
    private readonly byte[] header;

    public VirtualVbfStream(string realPath, string patchPath)
    {
        real = File.OpenRead(realPath);
        patchFile = File.OpenRead(patchPath);
        var fixedHeader = new byte[VbfPatchHeader.Size];
        patchFile.ReadExactly(fixedHeader);
        patch = VbfPatchHeader.Read(fixedHeader);
        header = new byte[patch.VirtualHeaderLength];
        patchFile.Position = (long)patch.HeaderBlobPos;
        patchFile.ReadExactly(header);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => (long)patch.VirtualFileSize;
    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ulong pos = (ulong)Position;
        ulong realDataEnd = patch.RealFileSize + patch.Delta;
        int produced = 0;
        while (produced < buffer.Length && pos < patch.VirtualFileSize)
        {
            var dest = buffer[produced..];
            int chunk;
            if (pos < patch.VirtualHeaderLength)
            {
                chunk = (int)Math.Min((ulong)dest.Length, patch.VirtualHeaderLength - pos);
                header.AsSpan((int)pos, chunk).CopyTo(dest);
            }
            else if (pos < realDataEnd)
            {
                chunk = (int)Math.Min((ulong)dest.Length, realDataEnd - pos);
                real.Position = (long)(pos - patch.Delta);
                real.ReadExactly(dest[..chunk]);
            }
            else if (pos >= patch.AppendedVirtualOffset)
            {
                chunk = (int)Math.Min((ulong)dest.Length, patch.VirtualFileSize - pos);
                patchFile.Position = (long)(patch.AppendedBlobPos + (pos - patch.AppendedVirtualOffset));
                patchFile.ReadExactly(dest[..chunk]);
            }
            else
            {
                chunk = (int)Math.Min((ulong)dest.Length, patch.AppendedVirtualOffset - pos);
                dest[..chunk].Clear();
            }

            pos += (ulong)chunk;
            produced += chunk;
        }

        Position = (long)pos;
        return produced;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => Position + offset,
        _ => Length + offset
    };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            real.Dispose();
            patchFile.Dispose();
        }

        base.Dispose(disposing);
    }
}
