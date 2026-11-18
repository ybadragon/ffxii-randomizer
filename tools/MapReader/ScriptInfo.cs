using Ff12Randomizer.Core.GameData;

namespace Ff12MapData;

public readonly record struct Point2(double X, double Z);
public readonly record struct Segment(Point2 A, Point2 B)
{
    public Point2 Mid => new((A.X + B.X) / 2, (A.Z + B.Z) / 2);
    public double DistanceTo(Point2 p)
    {
        double dx = B.X - A.X, dz = B.Z - A.Z, l = dx * dx + dz * dz;
        double t = l == 0 ? 0 : Math.Clamp(((p.X - A.X) * dx + (p.Z - A.Z) * dz) / l, 0, 1);
        double ex = p.X - A.X - t * dx, ez = p.Z - A.Z - t * dz;
        return Math.Sqrt(ex * ex + ez * ez);
    }
}

/// <summary>An exit: the trigger line that runs a mapjump, and every map and entrance it can lead to (some lead to
/// different maps at different story points). Interact: a door or gate, used with Space at the line (its actor shows
/// the prompt with fieldsign) rather than by walking across it.</summary>
public sealed record Exit(int TriggerId, string Actor, IReadOnlyList<Segment> Line, IReadOnlyList<(int Map, int Entrance)> Leads, bool Interact = false)
{
    public Point2 Mid => new(Line.Average(s => s.Mid.X), Line.Average(s => s.Mid.Z));
    public double DistanceTo(Point2 p) => Line.Min(s => s.DistanceTo(p));
}

/// <summary>An actor that switches the map's minimap/routing variant (setnavimapindex), and what it depends on.</summary>
public sealed record VariantSwitch(string Actor, IReadOnlyList<int> Variants, IReadOnlyList<StoryCheck> StoryChecks, bool ChecksPreviousMap, bool UsedByPlayer);

/// <summary>
/// What a map's script (.ebp) says about its layout. Format (script base = u32 at 0x10, offsets from the base):
/// base+0x54 -> entrances (u32 count, 0x20-byte entries x, y, z, facing); base+0x70 -> trigger-line lists (u32 count,
/// u32 offsets; each list: u32 n, then from +0x10 n segments of 0x20 bytes (ax, ay, az, 1)(bx, by, bz, flags), trigger
/// id = (flags >> 8) &amp; 0xFF). The actor whose code starts "PUSHII id; CALLPOPA setmapjumpgroup" owns a trigger id;
/// its "PUSHII map; PUSHII entrance; PUSHII 0; CALLPOPA mapjump" is where the exit leads. Doors and gates (actor type 1
/// in the actor table) instead show a Space prompt with "PUSHII k; CALLPOPA fieldsign", which belongs to trigger line
/// 0x14 + k (checked on Southgate: gate_south 0 = line 0x14, the downtown door 1 = 0x15; Necrohol's ten doors 0-9).
/// Call numbers per ff12-script.
/// </summary>
public sealed class ScriptInfo
{
    // script VM calls (CALLPOPA operands), from ffgriever's ff12-script
    const ushort MapJump = 0x008D, SetMapJumpGroup = 0x011E, SetNaviMapIndex = 0x021B, LastMap = 0x0092, FieldSign = 0x0176;
    const int FieldSignLines = 0x14;
    static readonly HashSet<ushort> PlayerUse = [0x0176 /* fieldsign */, 0x002A /* reqenable */, 0x0013 /* askpos */, 0x014A /* aaske */];
    const byte PushII = 0x4F, CallPopA = 0x5D, Call = 0x58;

    public IReadOnlyList<(double X, double Y, double Z, double Facing)> Entrances { get; }
    public IReadOnlyList<Exit> Exits { get; }
    /// <summary>Trigger lines no mapjump uses (scene and message triggers, guards that turn you back).</summary>
    public IReadOnlyList<(int TriggerId, string? Actor, Segment Line)> OtherTriggers { get; }
    /// <summary>The script's FF12POS3 table: named spots (index, position, size).</summary>
    public IReadOnlyList<(int Index, double X, double Y, double Z, double Width, double Depth)> Points { get; }
    public IReadOnlyList<VariantSwitch> VariantSwitches { get; }
    /// <summary>
    /// Spots where a Space prompt shows for an object with no trigger line (base+0x74: u32 count, then 0x1C-byte entries
    /// x, y, z, width, depth, 0, 0). Checked in Hourglass Basin (ztc_a03): "Space Boulder" shows at (317.1, 188.1), the
    /// first entry; using it changes the quicksand (the 流砂ギミック実行 actor switches the minimap variant).
    /// </summary>
    public IReadOnlyList<(double X, double Y, double Z, double Width, double Depth)> InteractSpots { get; }

    public static ScriptInfo? For(string scriptName) => scriptName.Length < 5 ? null :
        GameFiles.Get($"ps2data/plan_master/us/plan_map/{scriptName[..5]}/{scriptName}/global/{scriptName}.ebp") is { Length: > 0x80 } b ? new ScriptInfo(b) : null;

    public ScriptInfo(byte[] b)
    {
        int I(int o) => BitConverter.ToInt32(b, o);
        float F(int o) => BitConverter.ToSingle(b, o);
        var script = new MapScript(b);
        var checks = script.StoryChecks().GroupBy(c => c.Actor).ToDictionary(g => g.Key, g => (IReadOnlyList<StoryCheck>)g.ToList());

        // which actor owns each trigger id, where its mapjumps lead, and who switches variants
        var owner = new Dictionary<int, string>();
        var leads = new Dictionary<int, List<(int, int)>>();
        var switches = new List<VariantSwitch>();
        var interact = new HashSet<int>();
        foreach (var a in script.Actors)
        {
            var ins = script.Instructions(a).ToList();
            int trig = -1;
            for (int i = 1; i < Math.Min(ins.Count, 8); i++)
                if (ins[i].Op == CallPopA && ins[i].Operand == SetMapJumpGroup && ins[i - 1].Op == PushII) trig = ins[i - 1].Operand;
            // doors and gates: "PUSHII k; CALLPOPA fieldsign" shows the Space prompt on trigger line 0x14 + k (Southgate:
            // gate_south fieldsign 0 = line 0x14, the downtown door fieldsign 1 = line 0x15)
            if (trig < 0)
                for (int i = 1; i < Math.Min(ins.Count, 12); i++)
                    if (ins[i].Op == CallPopA && ins[i].Operand == FieldSign && ins[i - 1].Op == PushII) { trig = FieldSignLines + ins[i - 1].Operand; interact.Add(trig); break; }
            if (trig >= 0) owner.TryAdd(trig, a.Name);
            var variants = new SortedSet<int>();
            for (int i = 3; i < ins.Count; i++)
            {
                if (trig >= 0 && ins[i].Op == CallPopA && ins[i].Operand == MapJump && ins[i - 1].Op == PushII && ins[i - 2].Op == PushII && ins[i - 3].Op == PushII && ins[i - 1].Operand == 0)
                {
                    if (!leads.TryGetValue(trig, out var l)) leads[trig] = l = [];
                    if (!l.Contains((ins[i - 3].Operand, ins[i - 2].Operand))) l.Add((ins[i - 3].Operand, ins[i - 2].Operand));
                }
            }
            for (int i = 1; i < ins.Count; i++)
                if (ins[i].Op == CallPopA && ins[i].Operand == SetNaviMapIndex && ins[i - 1].Op == PushII) variants.Add((short)ins[i - 1].Operand);
            if (variants.Count > 0)
                switches.Add(new VariantSwitch(a.Name, [.. variants], checks.GetValueOrDefault(a.Name) ?? [],
                    ins.Any(x => x.Op is Call or CallPopA && x.Operand == LastMap), ins.Any(x => x.Op is Call or CallPopA && PlayerUse.Contains(x.Operand))));
        }
        VariantSwitches = switches;
        var spots = new List<(double, double, double, double, double)>();
        int sp = I(0x10) + I(I(0x10) + 0x74);
        if (sp > I(0x10) && sp + 4 <= b.Length)
            for (int k = 0, n = I(sp); k < n && k < 64 && sp + 4 + (k + 1) * 0x1C <= b.Length; k++)
            {
                int o = sp + 4 + k * 0x1C;
                spots.Add((F(o), F(o + 4), F(o + 8), F(o + 12), F(o + 16)));
            }
        InteractSpots = spots;

        int bs = I(0x10);
        var ents = new List<(double, double, double, double)>();
        int et = bs + I(bs + 0x54), ne = I(et);
        for (int e = 0; e < ne && e < 64; e++) { int o = et + 4 + e * 0x20; ents.Add((F(o), F(o + 4), F(o + 8), F(o + 12))); }
        Entrances = ents;

        var lines = new Dictionary<int, List<Segment>>();
        int tl = bs + I(bs + 0x70), nl = I(tl);
        for (int l = 0; l < nl && l < 64; l++)
        {
            int o = bs + I(tl + 4 + l * 4), n = I(o);
            for (int k = 0; k < n && k < 512; k++)
            {
                int sg = o + 0x10 + k * 0x20;
                if (F(sg) == 0 && F(sg + 8) == 0 || F(sg + 16) == 0 && F(sg + 24) == 0) continue;
                int id = (I(sg + 0x1C) >> 8) & 0xFF;
                if (!lines.TryGetValue(id, out var ls)) lines[id] = ls = [];
                ls.Add(new Segment(new(F(sg), F(sg + 8)), new(F(sg + 16), F(sg + 24))));
            }
        }
        Exits = lines.Where(kv => leads.ContainsKey(kv.Key)).Select(kv => new Exit(kv.Key, owner[kv.Key], kv.Value, leads[kv.Key], interact.Contains(kv.Key))).ToList();
        OtherTriggers = lines.Where(kv => !leads.ContainsKey(kv.Key)).SelectMany(kv => kv.Value.Select(s => (kv.Key, owner.GetValueOrDefault(kv.Key), s))).ToList();

        var pts = new List<(int, double, double, double, double, double)>();
        for (int i = 0; i + 8 < b.Length; i += 4)
        {
            if (b[i] != 'F' || System.Text.Encoding.ASCII.GetString(b, i, 8) != "FF12POS3") continue;
            for (int k = 0; k < I(i + 12); k++)
            {
                int e = i + 0x10 + k * 0x30;
                if (e + 0x30 > b.Length) break;
                if (F(e + 16) != 0 || F(e + 24) != 0) pts.Add((k, F(e + 16), F(e + 20), F(e + 24), F(e + 4), F(e + 8)));
            }
            break;
        }
        Points = pts;
    }
}
