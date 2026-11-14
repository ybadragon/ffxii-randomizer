#:property TargetFramework=net10.0-windows
#:project ../../app/Ff12Randomizer.Core/Ff12Randomizer.Core.csproj
using System.Diagnostics;
using Ff12Randomizer.Core.GameData;
// Reads every map's exits from its script (.ebp, extracted with the randomizer CLI; build app/Ff12Randomizer.Cli first).
// Run from tools/mapdata:
// dotnet run exits.cs -- <name|hexid>...   : each map's entrances and exits (trigger lines, the map and entrance they lead to)
// dotnet run exits.cs -- route <from> <to> : fewest-maps route over the exits, with the trigger line to walk to on each map
// dotnet run exits.cs -- csv exits.csv     : the table the test bot's `trip` uses (every map)
// dotnet run exits.cs -- names mapnames.csv : map id, script name and the game's place name (for the live viewer)
// Format (found 2026-11): script base = u32 at 0x10; base+0x54 -> entrance table (u32 count, 0x20-byte entries x,y,z,facing);
// base+0x70 -> trigger-line lists (u32 count, u32 offsets from base; each list: u32 n, then at +0x10 n segments of 0x20
// bytes (ax,ay,az,1)(bx,by,bz,flags), trigger id = (flags >> 8) & 0xFF). The actor whose code starts "PUSHII id;
// CALLPOPA 0x11E" owns that trigger id; its "PUSHII map; PUSHII entrance; PUSHII 0; CALLPOPA 0x8D" is the map jump.
string repo = Environment.CurrentDirectory;
while (!Directory.Exists(Path.Combine(repo, "app", "Ff12Randomizer.Core"))) repo = Path.GetDirectoryName(repo) ?? throw new InvalidOperationException("run from inside the randomizer repo");
string Cli = Path.Combine(repo, "app", "Ff12Randomizer.Cli", "bin", "Debug", "net10.0-windows", "Ff12Randomizer.Cli.exe");
var names = File.ReadAllLines(Path.Combine(repo, "app", "Ff12Randomizer.Core", "Data", "mapAreas.csv")).Skip(1).Select(l => l.Split(','))
    .Where(f => f.Length >= 2).ToDictionary(f => Convert.ToInt32(f[0], 16), f => f[1]);
string Name(string a) => names.Values.Contains(a) ? a : names[Convert.ToInt32(a, 16)];
// extracted scripts are cached outside git
string ebpDir = Path.Combine(repo, "work", "ebp");
Directory.CreateDirectory(ebpDir);

if (args[0] == "names")
{
    // names <out> : every map id with its script name and the game's own place name (planmapname.bin), for display
    var vbf = new Ff12Randomizer.Core.Vbf.VbfArchive(Ff12Randomizer.Core.Game.GameInstall.FindSteamInstall()!.DataArchivePath);
    var places = new PlaceNames(vbf);
    var rows = new List<string> { "map,name,place" };
    foreach (var (id, nm) in names.OrderBy(k => k.Key))
        rows.Add($"{id:X},{nm},{(places.Sub(id) ?? "").Replace(",", " ")}");
    File.WriteAllLines(args[1], rows);
    Console.WriteLine($"{rows.Count - 1} maps");
    return;
}
if (args[0] == "csv")
{
    // csv <out> : every map: mapId,mapName,exitId,midX,midZ,inX,inZ,destId,destName,destEntrance (inX/inZ = this map's entrance nearest the line: inside)
    var lines = new List<string> { "map,name,exit,midx,midz,inx,inz,dest,destname,entrance,segments" };
    var ids = names.ToDictionary(kv => kv.Value, kv => kv.Key);
    foreach (var (id, nm) in names.OrderBy(k => k.Key))
    {
        var info = Parse(nm); if (info is null) { Console.Error.WriteLine($"{nm}: no script"); continue; }
        foreach (var ex in info.Value.exits)
        {
            var (mx, mz) = ex.Mid;
            var inside = info.Value.entrances.Where(e => e.x != 0 || e.z != 0).OrderBy(e => (e.x - mx) * (e.x - mx) + (e.z - mz) * (e.z - mz)).Select(e => (e.x, e.z)).DefaultIfEmpty((mx, mz)).First();
            string segs = string.Join(" ", ex.Segs.Select(s => FormattableString.Invariant($"{s.ax:F2}:{s.az:F2}:{s.bx:F2}:{s.bz:F2}")));
            foreach (var (dm, de) in ex.Dest)
                lines.Add(FormattableString.Invariant($"{id:X},{nm},{ex.Id},{mx:F2},{mz:F2},{inside.Item1:F2},{inside.Item2:F2},{ids[dm]:X},{dm},{de},{segs}"));
        }
        Console.Error.Write(".");
    }
    File.WriteAllLines(args[1], lines);
    Console.WriteLine($"{lines.Count - 1} exits");
    return;
}
if (args[0] == "route")
{
    string from = Name(args[1]), to = Name(args[2]);
    var prev = new Dictionary<string, (string map, Exit ex, int ent)> { [from] = (null!, null!, -1) };
    var q = new Queue<string>(); q.Enqueue(from);
    while (q.Count > 0 && !prev.ContainsKey(to))
    {
        var m = q.Dequeue(); var info = Parse(m); if (info is null) continue;
        foreach (var ex in info.Value.exits) foreach (var (dm, de) in ex.Dest)
            if (!prev.ContainsKey(dm)) { prev[dm] = (m, ex, de); q.Enqueue(dm); }
    }
    if (!prev.ContainsKey(to)) { Console.WriteLine("no route"); return; }
    var path = new List<string>(); for (var m = to; m != from; m = prev[m].map) path.Add(m); path.Reverse();
    Console.WriteLine($"route {from} -> {to}: {path.Count} map changes");
    foreach (var m in path)
    {
        var (pm, ex, ent) = prev[m];
        var (cx, cz) = ex.Mid;
        var ei = Parse(m)!.Value.entrances;
        Console.WriteLine($"  on {pm}: walk to exit line mid ({cx:F1},{cz:F1}) -> {m} entrance {ent} at ({(ent < ei.Count ? ei[ent].x : 0):F1},{(ent < ei.Count ? ei[ent].z : 0):F1})");
    }
    return;
}
foreach (var arg in args)
{
    var name = Name(arg); var info = Parse(name);
    Console.WriteLine($"## {name}");
    if (info is null) { Console.WriteLine("  no script"); continue; }
    for (int e = 0; e < info.Value.entrances.Count; e++) Console.WriteLine($"  entrance {e}: ({info.Value.entrances[e].x:F1}, {info.Value.entrances[e].z:F1})");
    foreach (var ex in info.Value.exits)
        Console.WriteLine($"  exit id {ex.Id}: {string.Join(" ", ex.Segs.Select(s => $"({s.ax:F1},{s.az:F1})-({s.bx:F1},{s.bz:F1})"))}\n     -> {string.Join("; ", ex.Dest.Select(d => $"{d.map} entrance {d.ent}"))}");
}

(List<(float x, float y, float z)> entrances, List<Exit> exits)? Parse(string name)
{
    var file = Path.Combine(ebpDir, name + ".ebp");
    if (!File.Exists(file))
    {
        var p = Process.Start(new ProcessStartInfo(Cli, $"extract ps2data/plan_master/us/plan_map/{name[..5]}/{name}/global/{name}.ebp {file}") { RedirectStandardOutput = true, RedirectStandardInput = true })!;
        p.StandardOutput.ReadToEnd(); p.WaitForExit();
        if (!File.Exists(file)) return null;
    }
    var b = File.ReadAllBytes(file);
    int I(int o) => BitConverter.ToInt32(b, o); float F(int o) => BitConverter.ToSingle(b, o);
    var s = new MapScript(b);
    var dest = new Dictionary<int, List<(string, int)>>();
    foreach (var a in s.Actors)
    {
        var ins = s.Instructions(a).ToList();
        int trig = -1;
        for (int i = 1; i < Math.Min(ins.Count, 8); i++) if (ins[i].Op == 0x5D && ins[i].Operand == 0x11E && ins[i - 1].Op == 0x4F) trig = ins[i - 1].Operand;
        if (trig < 0) continue;
        for (int i = 3; i < ins.Count; i++)
            if (ins[i].Op == 0x5D && ins[i].Operand == 0x8D && ins[i - 1].Op == 0x4F && ins[i - 2].Op == 0x4F && ins[i - 3].Op == 0x4F && ins[i - 1].Operand == 0
                && names.TryGetValue(ins[i - 3].Operand, out var n))
            {
                if (!dest.TryGetValue(trig, out var l)) dest[trig] = l = [];
                if (!l.Contains((n, ins[i - 2].Operand))) l.Add((n, ins[i - 2].Operand));
            }
    }
    int bs = I(0x10);
    if (bs <= 0 || bs + 0x80 > b.Length) return null;
    var ents = new List<(float, float, float)>();
    int ent0 = bs + I(bs + 0x54), ne = I(ent0);
    for (int e = 0; e < ne && e < 64; e++) { int o = ent0 + 4 + e * 0x20; ents.Add((F(o), F(o + 4), F(o + 8))); }
    var exits = new List<Exit>();
    int tl = bs + I(bs + 0x70), nl = I(tl);
    for (int l = 0; l < nl && l < 64; l++)
    {
        int o = bs + I(tl + 4 + l * 4), n = I(o);
        var byId = new SortedDictionary<int, List<(float, float, float, float)>>();
        for (int k = 0; k < n && k < 512; k++)
        {
            int sg = o + 0x10 + k * 0x20; float ax = F(sg), az = F(sg + 8), bx = F(sg + 0x10), bz = F(sg + 0x18);
            if (ax == 0 && az == 0 || bx == 0 && bz == 0) continue;
            int tid = (I(sg + 0x1C) >> 8) & 0xFF;
            if (!byId.TryGetValue(tid, out var ls)) byId[tid] = ls = [];
            ls.Add((ax, az, bx, bz));
        }
        foreach (var (tid, segs) in byId)
            if (dest.TryGetValue(tid, out var d)) exits.Add(new Exit(tid, segs, d));
    }
    return (ents, exits);
}

record Exit(int Id, List<(float ax, float az, float bx, float bz)> Segs, List<(string map, int ent)> Dest)
{
    public (float, float) Mid => (Segs.Average(s => (s.ax + s.bx) / 2), Segs.Average(s => (s.az + s.bz) / 2));
}
