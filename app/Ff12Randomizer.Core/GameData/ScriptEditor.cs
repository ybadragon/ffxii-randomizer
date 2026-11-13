using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>
/// Inserts instructions into a map or event script (.ebp), which can't be done in place. Layout per ffgriever's ff12-script
/// (BSD-2-Clause), CScriptContainer.h:
/// <list type="bullet">
/// <item>The .ebp header holds the script's offset (u32 at 0x10) and the offsets of the sections after it.</item>
/// <item>The script header holds its size (u32 at 0x04) and the actor table (u32 at 0x18: u32 count, then 0x30-byte entries:
/// name 0x00, code start 0x08, instruction count 0x0C, function table 0x10, jump table 0x14). Offsets are from the script start.</item>
/// <item>The function table is u32 count, then (u32 name, u32 start) pairs; the jump table is u32 count, then u32 targets. Both
/// are byte offsets from the actor's code start, and jump instructions name a jump-table index, not an address.</item>
/// <item>External labels (u32 at 0x6C: u32 count, then script offsets) are jumps between actors.</item>
/// </list>
/// An edited actor's code is rebuilt with the new instructions and appended to the end of the script; its code start, function
/// and jump tables, and instruction count are updated, and the old copy is left unused. The sections after the script move
/// down, and their offsets in the .ebp header with them. Nothing else in the file changes.
/// </summary>
public sealed class ScriptEditor
{
    private readonly byte[] _ebp;
    private readonly int _script;
    private readonly List<(int At, int Remove, byte[] Code, int Instructions)> _edits = [];

    public ScriptEditor(byte[] ebp)
    {
        _ebp = ebp;
        _script = (int)U32(0x10);
    }

    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_ebp.AsSpan(at));

    /// <summary>Inserts code at a file offset (before the instruction that starts there).</summary>
    public void Insert(int at, byte[] code, int instructions) => _edits.Add((at, 0, code, instructions));

    /// <summary>Replaces the instructions in [at, at + remove) of the file; instructionDelta is new count minus old count.</summary>
    public void Replace(int at, int remove, byte[] code, int instructionDelta) => _edits.Add((at, remove, code, instructionDelta));

    public bool HasEdits => _edits.Count > 0;

    public byte[] Build()
    {
        if (_edits.Count == 0) return _ebp;
        int scriptSize = (int)U32(_script + 0x04);
        int table = _script + (int)U32(_script + 0x18);
        int actorCount = (int)U32(table);
        var appended = new List<byte>();
        var patched = (byte[])_ebp.Clone();
        void Put(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(at), value);

        int externals = (int)U32(_script + 0x6C);
        var externalAt = new List<int>();
        if (externals != 0)
            for (int i = 0, n = (int)U32(_script + externals); i < n; i++) externalAt.Add(_script + externals + 4 + i * 4);

        var remaining = _edits.ToList();
        for (int a = 0; a < actorCount && remaining.Count > 0; a++)
        {
            int entry = table + 4 + a * 0x30;
            int start = _script + (int)U32(entry + 0x08);
            int count = (int)U32(entry + 0x0C);
            int length = CodeLength(start, count);
            var mine = remaining.Where(e => e.At >= start && e.At < start + length).OrderBy(e => e.At).ToList();
            if (mine.Count == 0) continue;
            remaining.RemoveAll(mine.Contains);

            // The new code, and where each old offset (from the actor start) lands in it.
            var code = new List<byte>();
            int from = start, added = 0;
            foreach (var e in mine)
            {
                if (e.At < from) throw new InvalidDataException("Overlapping script edits.");
                code.AddRange(_ebp.AsSpan(from, e.At - from).ToArray());
                code.AddRange(e.Code);
                from = e.At + e.Remove;
                added += e.Instructions;
            }
            code.AddRange(_ebp.AsSpan(from, start + length - from).ToArray());
            int Moved(int offset, bool jumpTarget)
            {
                int shift = 0;
                foreach (var e in mine)
                {
                    int at = e.At - start;
                    if (offset > at && offset < at + e.Remove) throw new InvalidDataException("A jump lands inside replaced script code.");
                    // A target at an insertion point would skip the inserted code; at a replacement it starts the new code.
                    if (offset == at && e.Remove == 0 && jumpTarget) throw new InvalidDataException("A jump lands where script code is inserted.");
                    if (offset > at || (offset == at && e.Remove == 0)) shift += e.Code.Length - e.Remove;
                }
                return offset + shift;
            }

            int newStart = _script + scriptSize + appended.Count;
            appended.AddRange(code);
            while (appended.Count % 4 != 0) appended.Add(0);

            Put(entry + 0x08, (uint)(newStart - _script));
            Put(entry + 0x0C, (uint)(count + added));
            int functions = _script + (int)U32(entry + 0x10);
            for (int i = 0, n = (int)U32(functions); i < n; i++)
                Put(functions + 8 + i * 8, (uint)Moved((int)U32(functions + 8 + i * 8), false));
            int jumps = _script + (int)U32(entry + 0x14);
            for (int i = 0, n = (int)U32(jumps); i < n; i++)
                Put(jumps + 4 + i * 4, (uint)Moved((int)U32(jumps + 4 + i * 4), true));
            foreach (int at in externalAt)
            {
                int target = _script + (int)U32(at);
                if (target >= start && target < start + length) Put(at, (uint)(newStart - _script + Moved(target - start, true)));
            }
        }
        if (remaining.Count > 0) throw new InvalidDataException("A script edit is outside every actor's code.");

        // Grow the script by the appended code and move the sections after it.
        int grow = (appended.Count + 15) / 16 * 16;
        while (appended.Count < grow) appended.Add(0);
        int end = _script + scriptSize;
        Put(_script + 0x04, (uint)(scriptSize + grow));
        for (int at = 0x04; at < _script; at += 4)
        {
            uint value = U32(at);
            if (at != 0x10 && value >= end && value <= _ebp.Length) Put(at, (uint)(value + grow));
        }
        var result = new byte[_ebp.Length + grow];
        patched.AsSpan(0, end).CopyTo(result);
        appended.ToArray().CopyTo(result, end);
        patched.AsSpan(end).CopyTo(result.AsSpan(end + grow));
        return result;
    }

    /// <summary>Byte length of an actor's code: its instruction count decoded from the start (1 byte below 0x48, else 3).</summary>
    private int CodeLength(int start, int count)
    {
        int p = start;
        for (int i = 0; i < count; i++) p += _ebp[p] < 0x48 ? 1 : 3;
        return p - start;
    }
}
