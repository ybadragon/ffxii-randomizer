using System.Buffers.Binary;

namespace Ff12Randomizer.Core.GameData;

/// <summary>
/// Inserts instructions into a map or event script (.ebp) without changing the file's size. The game sizes each file's
/// buffer from its own size table (FileSizeTables\*.fst), so a file that grows can crash it (tested in game).
/// <para>Layout per ffgriever's ff12-script (BSD-2-Clause), CScriptContainer.h. The .ebp header holds the script's offset
/// (u32 at 0x10). The script header holds the actor table (u32 at 0x18: u32 count, then 0x30-byte entries: code start 0x08,
/// instruction count 0x0C, function table 0x10, jump table 0x14; offsets from the script start) and the external labels
/// (u32 at 0x6C: u32 count, then script offsets of jump targets in other actors). The function table is u32 count, then
/// (u32 name, u32 start) pairs; the jump table is u32 count, then u32 targets. Both are byte offsets from the actor's code
/// start, and jump instructions name a jump-table index, not an address. All actors' code sits in one block.</para>
/// <para>The room for new instructions comes from the original compiler's jumps to the very next instruction (the end of
/// most switch cases): removing one changes nothing and frees 3 bytes. Inserted code is padded with OPNOT pairs to a
/// multiple of 3 (it always ends in a boolean). The code between the edits moves, and every actor start, function and
/// jump target and external label is moved with it. No table moves, and nothing outside the code block changes except
/// those offsets and the instruction counts.</para>
/// </summary>
public sealed class ScriptEditor
{
    private const byte Jmp = 0x51, OpNot = 0x17;
    private readonly byte[] _ebp;
    private readonly int _script;
    private readonly List<(int At, int Remove, byte[] Code, int Instructions, bool Boolean)> _edits = [];

    public ScriptEditor(byte[] ebp)
    {
        _ebp = ebp;
        _script = (int)U32(0x10);
    }

    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_ebp.AsSpan(at));

    /// <summary>
    /// Inserts code at a file offset (before the instruction that starts there). <paramref name="leavesBoolean"/>: the
    /// code ends with a boolean on the stack, so OPNOT pairs may follow it as padding.
    /// </summary>
    public void Insert(int at, byte[] code, int instructions, bool leavesBoolean) => _edits.Add((at, 0, code, instructions, leavesBoolean));

    /// <summary>Replaces the instructions in [at, at + remove) of the file; instructionDelta is new count minus old count.</summary>
    public void Replace(int at, int remove, byte[] code, int instructionDelta) => _edits.Add((at, remove, code, instructionDelta, false));

    public bool HasEdits => _edits.Count > 0;

    private sealed record Actor(int Entry, int Start, int Count, int Length, int Functions, int Jumps);

    public byte[] Build()
    {
        if (_edits.Count == 0) return _ebp;
        int table = _script + (int)U32(_script + 0x18);
        var actors = new List<Actor>();
        for (int a = 0, n = (int)U32(table); a < n; a++)
        {
            int entry = table + 4 + a * 0x30;
            int start = _script + (int)U32(entry + 0x08), count = (int)U32(entry + 0x0C);
            actors.Add(new Actor(entry, start, count, CodeLength(start, count), _script + (int)U32(entry + 0x10), _script + (int)U32(entry + 0x14)));
        }
        Actor Owner(int at) => actors.FirstOrDefault(a => at >= a.Start && at < a.Start + a.Length)
            ?? throw new InvalidDataException("A script edit is outside every actor's code.");

        // Pay for the new bytes with jumps to the next instruction, padded with OPNOT pairs to a multiple of 3.
        var edits = _edits.ToList();
        int grow = edits.Sum(e => e.Code.Length - e.Remove);
        int pad = grow % 3 == 0 ? 0 : grow % 3 == 1 ? 2 : 4;
        if (pad > 0)
        {
            int i = edits.FindIndex(e => e.Boolean);
            if (i < 0) throw new InvalidDataException("No room to pad the script edits.");
            var e = edits[i];
            edits[i] = e with { Code = [.. e.Code, .. Enumerable.Repeat(OpNot, pad)], Instructions = e.Instructions + pad };
        }
        int needed = (grow + pad) / 3;
        var touched = edits.Select(e => (e.At, End: e.At + Math.Max(e.Remove, 1))).ToList();
        var freed = NoOpJumps(actors).Distinct().Where(j => !touched.Any(t => j < t.End && j + 3 > t.At)).Take(needed).ToList();
        if (freed.Count < needed)
            throw new InvalidDataException($"Script edits need {needed * 3} bytes; only {freed.Count * 3} can be freed.");
        foreach (int at in freed) edits.Add((at, 3, [], -1, false));
        edits = [.. edits.OrderBy(e => e.At)];

        // Where an old file offset lands. A jump target at an insertion point would skip the inserted code; at a replacement
        // or removed jump it becomes the start of whatever replaced it.
        int Moved(int at, bool target)
        {
            int shift = 0;
            foreach (var e in edits)
            {
                if (at > e.At && at < e.At + e.Remove) throw new InvalidDataException("A jump lands inside replaced script code.");
                if (at == e.At && e.Remove == 0 && target) throw new InvalidDataException("A jump lands where script code is inserted.");
                if (at > e.At || (at == e.At && e.Remove == 0)) shift += e.Code.Length - e.Remove;
            }
            return at + shift;
        }

        var patched = (byte[])_ebp.Clone();
        void Put(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(patched.AsSpan(at), value);
        int blockStart = actors.Min(a => a.Start), blockEnd = actors.Max(a => a.Start + a.Length);
        var block = new List<byte>(blockEnd - blockStart);
        int from = blockStart;
        foreach (var e in edits)
        {
            block.AddRange(_ebp.AsSpan(from, e.At - from).ToArray());
            block.AddRange(e.Code);
            from = e.At + e.Remove;
        }
        block.AddRange(_ebp.AsSpan(from, blockEnd - from).ToArray());
        if (block.Count != blockEnd - blockStart) throw new InvalidDataException("Script edits changed the code size.");
        block.ToArray().CopyTo(patched, blockStart);

        foreach (var actor in actors)
        {
            int start = Moved(actor.Start, false);
            Put(actor.Entry + 0x08, (uint)(start - _script));
            Put(actor.Entry + 0x0C, (uint)(actor.Count + edits.Where(e => Owner(e.At) == actor).Sum(e => e.Instructions)));
            for (int i = 0, n = (int)U32(actor.Functions); i < n; i++)
                Put(actor.Functions + 8 + i * 8, (uint)(Moved(actor.Start + (int)U32(actor.Functions + 8 + i * 8), false) - start));
            for (int i = 0, n = (int)U32(actor.Jumps); i < n; i++)
                Put(actor.Jumps + 4 + i * 4, (uint)(Moved(actor.Start + (int)U32(actor.Jumps + 4 + i * 4), true) - start));
        }
        int externals = (int)U32(_script + 0x6C);
        if (externals != 0)
            for (int i = 0, n = (int)U32(_script + externals); i < n; i++)
            {
                int at = _script + externals + 4 + i * 4;
                Put(at, (uint)(Moved(_script + (int)U32(at), true) - _script));
            }
        return patched;
    }

    /// <summary>File offsets of every JMP whose target is the instruction right after it, inside the same actor.</summary>
    private IEnumerable<int> NoOpJumps(List<Actor> actors)
    {
        foreach (var actor in actors)
        {
            int jumps = (int)U32(actor.Jumps);
            for (int p = actor.Start, i = 0; i < actor.Count; i++)
            {
                byte op = _ebp[p];
                if (op == Jmp)
                {
                    int index = BinaryPrimitives.ReadUInt16LittleEndian(_ebp.AsSpan(p + 1));
                    if (index < jumps && actor.Start + (int)U32(actor.Jumps + 4 + index * 4) == p + 3 && p + 3 < actor.Start + actor.Length)
                        yield return p;
                }
                p += op < 0x48 ? 1 : 3;
            }
        }
    }

    /// <summary>Byte length of an actor's code: its instruction count decoded from the start (1 byte below 0x48, else 3).</summary>
    private int CodeLength(int start, int count)
    {
        int p = start;
        for (int i = 0; i < count; i++) p += _ebp[p] < 0x48 ? 1 : 3;
        return p - start;
    }
}
