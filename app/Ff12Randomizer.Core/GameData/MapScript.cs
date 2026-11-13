using System.Buffers.Binary;
using System.Text;

namespace Ff12Randomizer.Core.GameData;

/// <summary>One place a script compares the story flag with a constant.</summary>
/// <param name="Actor">The actor the comparison is in.</param>
/// <param name="At">Offset in the .ebp of the "PUSHII constant" instruction.</param>
/// <param name="Value">The constant.</param>
/// <param name="Compare">The comparison, as the script source would read: "&lt;", "&lt;=", "==", "!=", "&gt;", "&gt;=", or "case ..." forms.</param>
public sealed record StoryCheck(string Actor, int At, short Value, string Compare);

/// <summary>
/// The compiled script inside a map or event .ebp (layout and opcodes per ffgriever's ff12-script, BSD-2-Clause).
/// The script starts at the u32 at 0x10 of the file. Its header has the actor table at 0x18 (u32 count, then 0x30-byte
/// entries: name offset at 0x00, code start at 0x08) and the name text at 0x4C. Instructions are one byte, or three
/// (opcode and a u16 operand) from opcode 0x48 up.
/// The story flag is シナリオフラグ, savemem short 0x00 (see <see cref="StoryFlag"/>). Scripts compare it two ways:
/// "PUSHV flag; PUSHII n; OPxx" (an if), and "PUSHV flag; POPY" followed by "PUSHII n; POPCMPYxxJMP" cases (a switch).
/// </summary>
public sealed class MapScript
{
    public const byte PushV = 0x48, PopV = 0x49, PushII = 0x4F, PopY = 0x20, Jmp = 0x51, CallPopA = 0x5D;
    /// <summary>
    /// The operand that means the story flag in this script: the index of the entry (address 0x0000, type 0x3000 = saved
    /// short) in the script's variable table at header 0x28 (u32 count, then u16 address, u16 type, u32 per entry).
    /// 0xFFFF when the script doesn't use the story flag.
    /// </summary>
    public ushort StoryFlag { get; } = 0xFFFF;
    private static readonly Dictionary<byte, string> IfCompares = new()
    {
        [0x0A] = "==", [0x0B] = "!=", [0x0C] = ">", [0x0D] = "<", [0x0E] = ">=", [0x0F] = "<=",
    };
    private static readonly Dictionary<byte, string> CaseCompares = new()
    {
        [0x52] = "case ==", [0x53] = "case <", [0x54] = "case <=", [0x55] = "case >", [0x56] = "case >=", [0x57] = "case between",
    };

    private readonly byte[] _ebp;
    private readonly int _variables, _variableCount;

    public MapScript(byte[] ebp)
    {
        _ebp = ebp;
        int script = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(0x10));
        if (script <= 0 || script + 0x50 > ebp.Length) return;
        int table = script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(script + 0x18));
        int nameText = script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(script + 0x4C));
        int variableStart = script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(script + 0x28));
        // Not a compiled script (some .ebp hold no code); leave it empty.
        if (table <= script || table + 4 > ebp.Length || variableStart <= script || variableStart + 4 > ebp.Length || nameText > ebp.Length) return;
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(table));
        int variables = _variables = script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(script + 0x28));
        int variableCount = _variableCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(variables));
        for (int v = 0; v < variableCount && variables + 12 + v * 8 <= ebp.Length; v++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(variables + 4 + v * 8)) == 0x30000000) { StoryFlag = (ushort)v; break; }
        }
        var starts = new List<(string Name, int Start)>();
        var shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        for (int i = 0; i < count; i++)
        {
            int entry = table + 4 + i * 0x30;
            int name = nameText + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(entry));
            int end = Array.IndexOf(ebp, (byte)0, name);
            starts.Add((shiftJis.GetString(ebp, name, end - name), script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(entry + 8))));
        }
        var ordered = starts.OrderBy(a => a.Start).ToList();
        for (int a = 0; a < ordered.Count; a++)
            Actors.Add((ordered[a].Name, ordered[a].Start, a + 1 < ordered.Count ? ordered[a + 1].Start : Math.Min(ebp.Length, table)));
    }

    /// <summary>Each actor with the byte range of its code, in file order.</summary>
    public List<(string Name, int Start, int End)> Actors { get; } = [];

    /// <summary>Every instruction of an actor: its offset, opcode and operand (0 for one-byte instructions).</summary>
    public IEnumerable<(int At, byte Op, ushort Operand)> Instructions((string Name, int Start, int End) actor)
    {
        for (int p = actor.Start; p < actor.End;)
        {
            byte op = _ebp[p];
            if (op < 0x48) { yield return (p, op, 0); p += 1; }
            else if (p + 3 <= actor.End) { yield return (p, op, BinaryPrimitives.ReadUInt16LittleEndian(_ebp.AsSpan(p + 1))); p += 3; }
            else yield break;
        }
    }

    /// <summary>The operand of a variable in this script, by its address and type in the variable table; null when unused.</summary>
    public ushort? Variable(ushort address, ushort type)
    {
        for (int v = 0; v < _variableCount; v++)
        {
            int entry = _variables + 4 + v * 8;
            if (BinaryPrimitives.ReadUInt16LittleEndian(_ebp.AsSpan(entry)) == address && BinaryPrimitives.ReadUInt16LittleEndian(_ebp.AsSpan(entry + 2)) == type)
                return (ushort)v;
        }
        return null;
    }

    /// <summary>Every comparison of the story flag with a constant, in file order.</summary>
    public IEnumerable<StoryCheck> StoryChecks() => Checks(StoryFlag);

    /// <summary>Every comparison of a variable (by operand) with a constant, in file order.</summary>
    public IEnumerable<StoryCheck> Checks(ushort variable)
    {
        ushort StoryFlag = variable;
        foreach (var actor in Actors)
        {
            var code = Instructions(actor).ToList();
            bool flagInY = false;
            for (int i = 0; i < code.Count; i++)
            {
                var (at, op, operand) = code[i];
                // The Y register holds the switch value until something else is popped into it. A RET ends the function.
                if (op == PopY) flagInY = i > 0 && code[i - 1].Op == PushV && code[i - 1].Operand == StoryFlag;
                else if (op == 0x2D) flagInY = false;
                if (op != PushII || i + 1 >= code.Count) continue;
                byte next = code[i + 1].Op;
                // "constant <op> flag" (the constant pushed first), named "rev <op>".
                if (next == PushV && code[i + 1].Operand == StoryFlag && i + 2 < code.Count && IfCompares.TryGetValue(code[i + 2].Op, out string? reversed))
                {
                    yield return new StoryCheck(actor.Name, at, (short)operand, "rev " + reversed);
                    continue;
                }
                if (i > 0 && code[i - 1].Op == PushV && code[i - 1].Operand == StoryFlag && IfCompares.TryGetValue(next, out string? compare))
                    yield return new StoryCheck(actor.Name, at, (short)operand, compare);
                else if (flagInY && CaseCompares.TryGetValue(next, out compare))
                    yield return new StoryCheck(actor.Name, at, (short)operand, compare);
            }
        }
    }

    /// <summary>Every "story flag = constant" assignment: the actor, the offset of its PUSHII, and the value.</summary>
    public IEnumerable<(string Actor, int At, short Value)> StoryWrites() => Writes(StoryFlag);

    /// <summary>Every "variable = constant" assignment of a variable (by operand).</summary>
    public IEnumerable<(string Actor, int At, short Value)> Writes(ushort variable)
    {
        ushort StoryFlag = variable;
        foreach (var actor in Actors)
        {
            var code = Instructions(actor).ToList();
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].Op == PushII && code[i + 1].Op == PopV && code[i + 1].Operand == StoryFlag)
                    yield return (actor.Name, code[i].At, (short)code[i].Operand);
            }
        }
    }

    /// <summary>
    /// Turns the comparison found by <see cref="StoryChecks"/> at <paramref name="at"/> into "== value", in place: the
    /// operator after the PUSHII (OPLS/OPLSE/... or a POPCMPY case jump) becomes its "equal" form and the constant changes.
    /// </summary>
    public void SetEquals(int at, short value)
    {
        byte op = _ebp[at + 3];
        if (IfCompares.ContainsKey(op)) _ebp[at + 3] = 0x0A;
        else if (CaseCompares.ContainsKey(op) && op != 0x57) _ebp[at + 3] = 0x52;
        else throw new InvalidDataException($"No comparison right after the constant at {at:X}.");
        SetConstant(at, value);
    }

    /// <summary>Replaces the constant of a comparison found by <see cref="StoryChecks"/>.</summary>
    public void SetConstant(int at, short value)
    {
        if (_ebp[at] != PushII) throw new InvalidDataException("Not a PUSHII instruction.");
        BinaryPrimitives.WriteInt16LittleEndian(_ebp.AsSpan(at + 1), value);
    }
}
