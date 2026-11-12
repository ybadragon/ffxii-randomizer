using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Skips the opening movie and Reks's prologue: a new game goes straight to the point where the prologue hands over to
/// Vaan (the Garamsythe Waterway, before the sewer rats). Nothing is hand-written; the patch is built from the game's
/// own scripts (opcodes per ffgriever's ff12-script, BSD-2-Clause):
/// <list type="bullet">
/// <item>The prologue ends in naf_b04 with "story flag = 25; btlAtelSetNextSetSaveData(1); mapjump(sewer)": set the
/// story point, switch from Reks's party data to Vaan's, and change map. Those instructions are copied as they are.</item>
/// <item>naf_a01 is the first map of a new game. Its main function runs the new game branch (opening movie, then the
/// prologue) only while its own progress value is below 10; the branch then sets it to 10 along with story flag 13.
/// The copied ending is written over the start of that branch, followed by the branch's own closing jump, so the
/// movie and the rest of the branch never run. Nothing moves: the file keeps its length and every jump target.</item>
/// </list>
/// Only a new game reaches that branch, so saves are unaffected. The open world replaces naf_a01 with its own start,
/// so this does nothing with the open world on.
/// </summary>
public sealed class SkipPrologueFeature : IRandomizerFeature
{
    public const string Id = "skipprologue";
    public const string FirstMapPath = "ps2data/plan_master/us/plan_map/naf_a/naf_a01/global/naf_a01.ebp";
    public const string LastMapPath = "ps2data/plan_master/us/plan_map/naf_b/naf_b04/global/naf_b04.ebp";

    // Script VM opcodes. Each one used here is 3 bytes: the opcode, then a u16 operand.
    private const byte PushV = 0x48, PopV = 0x49, Jmp = 0x51, PopXncJmp = 0x5C, CallPopA = 0x5D, PushII = 0x4F;
    private const byte OpLs = 0x0D; // "<", 1 byte
    private const ushort StoryFlagVariable = 0x0000; // savemem short 0x00 (シナリオフラグ), as both scripts address it
    private const ushort MapJumpCall = 0x008D;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id) && !settings.IsOn(OpenWorldFeature.Id);

    public void Apply(RandomizerContext context)
    {
        var ending = PrologueEnding.Find(context.Original.Extract(context.Original.Find(LastMapPath)!));
        var script = context.Edit(FirstMapPath);
        var branch = NewGameBranch.Find(script);
        branch.Replace(script, ending.Code);

        var names = new MapNames(context.Original);
        string? mapId = MapNames.IdOf(ending.MapNumber);
        string? place = mapId is null ? null : names.NameOf(mapId);
        string? region = mapId is null ? null : names.RegionOf(mapId);
        context.Spoil("newGame", new
        {
            change = "Prologue skipped",
            startsAt = string.Join(", ", new[] { region, place }.OfType<string>().Distinct()),
            storyFlag = ending.StoryFlag,
        });
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        if (!context.ChangedFiles.TryGetValue(FirstMapPath, out var script)) yield break;
        var ending = PrologueEnding.Find(context.Original.Extract(context.Original.Find(LastMapPath)!));
        if (!NewGameBranch.Find(script).StartsWith(script, ending.Code))
            yield return "Skip prologue: the new game branch in naf_a01 doesn't start with the prologue's ending.";
    }

    private static ushort U16(byte[] b, int at) => (ushort)(b[at + 1] | b[at + 2] << 8);

    private static bool Is(byte[] b, int at, byte op) => at + 3 <= b.Length && b[at] == op;

    private static bool Is(byte[] b, int at, byte op, ushort operand) => Is(b, at, op) && U16(b, at) == operand;

    /// <summary>
    /// naf_b04's last instructions: PUSHII flag; POPV story flag; PUSHII 1; CALLPOPA (party data switch); PUSHII map;
    /// PUSHII entrance; PUSHII mode; CALLPOPA mapjump. Must occur exactly once.
    /// </summary>
    private sealed record PrologueEnding(byte[] Code, int StoryFlag, int MapNumber)
    {
        public static PrologueEnding Find(byte[] b)
        {
            var found = new List<int>();
            for (int p = 0; p + 24 <= b.Length; p++)
            {
                if (Is(b, p, PushII) && Is(b, p + 3, PopV, StoryFlagVariable) && Is(b, p + 6, PushII) && Is(b, p + 9, CallPopA)
                    && Is(b, p + 12, PushII) && Is(b, p + 15, PushII) && Is(b, p + 18, PushII) && Is(b, p + 21, CallPopA, MapJumpCall))
                {
                    found.Add(p);
                }
            }
            if (found.Count != 1)
                throw new InvalidDataException($"Skip prologue: expected the prologue's ending once in naf_b04, found it {found.Count} times.");
            int at = found[0];
            return new PrologueEnding(b[at..(at + 24)], U16(b, at), U16(b, at + 12));
        }
    }

    /// <summary>
    /// naf_a01's new game branch: "if (progress &lt; 10) { ... story flag = 13; progress = 10; ... }". Found from that
    /// assignment: the progress variable is the one set to 10 right after story flag 13, and the branch is the last
    /// "PUSHV progress; PUSHII 10; OPLS; POPXNCJMP else" before it. The branch ends with "JMP end", end being the label
    /// after the else label.
    /// </summary>
    private sealed record NewGameBranch(int BodyStart, int BodyEnd, byte[] CloseJump)
    {
        public static NewGameBranch Find(byte[] b)
        {
            // The assignment is still there after patching (as code that no longer runs), so this works on both.
            var stores = new List<(int At, ushort Progress)>();
            for (int p = 0; p + 12 <= b.Length; p++)
            {
                if (Is(b, p, PushII, 13) && Is(b, p + 3, PopV, StoryFlagVariable) && Is(b, p + 6, PushII, 10) && Is(b, p + 9, PopV))
                    stores.Add((p, U16(b, p + 9)));
            }
            if (stores.Count != 1)
                throw new InvalidDataException($"Skip prologue: expected naf_a01 to start the prologue once, found {stores.Count}.");
            var (store, progress) = stores[0];

            int check = -1;
            for (int p = 0; p + 10 <= store; p++)
            {
                if (Is(b, p, PushV, progress) && Is(b, p + 3, PushII, 10) && b[p + 6] == OpLs && Is(b, p + 7, PopXncJmp)) check = p;
            }
            if (check < 0) throw new InvalidDataException("Skip prologue: naf_a01's new game check wasn't found.");
            ushort elseLabel = U16(b, check + 7);
            int body = check + 10;

            // The branch's closing jump is the first "JMP else+1" after the assignment.
            int close = -1;
            for (int p = store; p + 3 <= b.Length; p++)
            {
                if (Is(b, p, Jmp, (ushort)(elseLabel + 1))) { close = p; break; }
            }
            if (close < 0) throw new InvalidDataException("Skip prologue: the end of naf_a01's new game branch wasn't found.");
            return new NewGameBranch(body, close + 3, b[close..(close + 3)]);
        }

        /// <summary>
        /// Writes <paramref name="code"/> and the closing jump at the start of the branch. The rest of the statement it
        /// lands in is filled with more copies of the jump, so the leftover code starts on a whole statement.
        /// </summary>
        public void Replace(byte[] b, byte[] code)
        {
            int length = code.Length + CloseJump.Length;
            // The overwritten instructions must be whole 3-byte PUSHII/CALLPOPA ones; stop at the end of the first call
            // at or past the patch, so no call is left without its arguments.
            int end = BodyStart;
            while (true)
            {
                if (end + 3 > BodyEnd || !(Is(b, end, PushII) || Is(b, end, CallPopA)))
                    throw new InvalidDataException("Skip prologue: naf_a01's new game branch doesn't start the way it should.");
                bool call = b[end] == CallPopA;
                end += 3;
                if (call && end >= BodyStart + length) break;
            }
            code.CopyTo(b, BodyStart);
            for (int p = BodyStart + code.Length; p < end; p += 3) CloseJump.CopyTo(b, p);
        }

        public bool StartsWith(byte[] b, byte[] code) =>
            b.AsSpan(BodyStart, code.Length).SequenceEqual(code) && b.AsSpan(BodyStart + code.Length, 3).SequenceEqual(CloseJump);
    }
}
