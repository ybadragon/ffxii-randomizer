using System.Diagnostics;
using System.Runtime.InteropServices;

// memtool: reads (and on request writes) FFXII_TZA.exe memory for testing.
//  regions                         writable memory summary
//  snap <file>                     dumps all writable private memory
//  moved <a> <b> <c>               floats that moved steadily a->b->c (player position candidates), as xyz triples first
//  get <type> <addr> [n]           type u8/u16/u32/f32; addr hex or exe+hex
//  put <type> <addr> <value>
//  find <type> <value>             addresses holding value (writable memory)
// never leave a movement key held down: release them on any exit, crashes included
AppDomain.CurrentDomain.UnhandledException += (_, _) => Input.ReleaseAll();
AppDomain.CurrentDomain.ProcessExit += (_, _) => Input.ReleaseAll();
var p = Process.GetProcessesByName("FFXII_TZA").FirstOrDefault() ?? throw new Exception("game not running");
IntPtr h = Native.OpenProcess(0x1F0FFF, false, p.Id);
long exe = (long)p.MainModule!.BaseAddress;
long Parse(string s) => s.StartsWith("exe+") ? exe + Convert.ToInt64(s[4..], 16) : Convert.ToInt64(s, 16);
string Name(long a) => a >= exe && a < exe + p.MainModule!.ModuleMemorySize ? $"exe+{a - exe:X}" : $"{a:X}";

IEnumerable<(long Base, long Size)> Regions()
{
    long a = 0;
    while (Native.VirtualQueryEx(h, (IntPtr)a, out var m, (IntPtr)Marshal.SizeOf<Native.MBI>()) != IntPtr.Zero)
    {
        long size = (long)m.Size;
        if (m.State == 0x1000 && (m.Protect & 0x04) != 0 && (m.Protect & 0x100) == 0 && size < (1L << 30)) yield return ((long)m.Base, size);
        a = (long)m.Base + size;
        if (a <= 0) break;
    }
}
byte[] Read(long a, int n) { var b = new byte[n]; Native.ReadProcessMemory(h, (IntPtr)a, b, (IntPtr)n, out _); return b; }
(float x, float y, float z) Pos0() { var b = Read(exe + 0x2A2E750, 12); return (BitConverter.ToSingle(b, 0), BitConverter.ToSingle(b, 4), BitConverter.ToSingle(b, 8)); }
// The controlled character: exe+21A7D20 (next to the party records) follows the leader in town-style maps too, where
// exe+2A2E750 stays frozen (seen in the Estersand Outpost, dst_a07).
(float x, float y, float z) Pos() { var b = Read(exe + 0x21A7D20, 12); return (BitConverter.ToSingle(b, 0), BitConverter.ToSingle(b, 4), BitConverter.ToSingle(b, 8)); }
void Cell0() { }

// Minimap reading: the style-free edge reader by default; MMMODE=color uses the older per-colour rules.
Minimap.Cell[,] ReadGrid(System.Drawing.Bitmap bmp) => Environment.GetEnvironmentVariable("MMMODE") switch { "color" => Minimap.Grid(bmp, ConeAngle()), "edge" => Minimap.EdgeGrid(bmp, ConeAngle()), _ => Minimap.WatershedGrid(bmp, ConeAngle()) };

// Direction the camera (and the minimap view cone) points, as a minimap pixel angle: W moves world (s, c), the minimap
// is north-up with +x right and +z down.
double ConeAngle() { var m = Read(exe + 0x1E5E528, 0x24); return Math.Atan2(BitConverter.ToSingle(m, 0), BitConverter.ToSingle(m, 0x20)); }

// Re-aligns a map texture with the live minimap: matches the minimap's wall lines against the texture's within +-r px
// of the current offset and moves the offset to the best match. Returns the shift applied and the match score.
(int dx, int dy, double score) Align(TexMap tm, int r, double mmScale = 4.63)
{
    using var mm = Minimap.Capture(p.MainWindowHandle);
    var (wx, _, wz) = Pos();
    var mk = Minimap.Marker(Minimap.Grid(mm)) ?? (135, 138);
    var me = Minimap.EdgeMap(mm);
    double ca = ConeAngle();
    var pts = new List<(double dx, double dy, float g)>();
    for (int y = 24; y < Minimap.Size - 24; y++)
        for (int x = 24; x < Minimap.Size - 24; x++)
        {
            double dx = x - mk.x, dy = y - mk.y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 16) continue;
            double off = Math.Abs(Math.IEEERemainder(Math.Atan2(dy, dx) - ca, 2 * Math.PI));
            if (dist < 110 && Math.Abs(off - Minimap.ConeHalfAngle) < 0.12) continue;
            pts.Add((dx / mmScale * 4, dy / mmScale * 4, me[x, y]));
        }
    var top = pts.OrderByDescending(t => t.g).Take(1200).ToList();
    double best = -1; int bx = 0, by = 0;
    for (int sy = -r; sy <= r; sy++)
        for (int sx = -r; sx <= r; sx++)
        {
            double cx = wx * 4 + tm.Ox + sx, cy = wz * 4 + tm.Oy + sy, s = 0;
            foreach (var t in top) { int u = (int)(cx + t.dx), v = (int)(cy + t.dy); if (u >= 0 && v >= 0 && u < tm.W && v < tm.H) s += tm.Edge[u, v]; }
            if (s > best) { best = s; bx = sx; by = sy; }
        }
    best /= Math.Max(1, top.Count);
    if (best > 0.45) { tm.Ox += bx; tm.Oy += by; } else { bx = 0; by = 0; }
    return (bx, by, best);
}

// One movement step toward (tx, tz): the key (or diagonal pair) closest to that world direction, held for ms.
void Steer(double tx, double tz, int ms)
{
    var c = Pos();
    double fwd = ConeAngle(); // same angle as W's world direction (s, c)
    double rel = Norm(Math.Atan2(tz - c.z, tx - c.x) - fwd) * 180 / Math.PI;
    var dirs = new (char[] k, double ang)[] { (['W'], 0), (['W', 'D'], 45), (['D'], 90), (['S', 'D'], 135), (['S'], 180), (['S', 'A'], -135), (['A'], -90), (['W', 'A'], -45) };
    var pick = dirs.OrderBy(o => Math.Abs(Norm((rel - o.ang) * Math.PI / 180))).First();
    Input.Hold(pick.k, ms);
}

// Measures how the camera turns: a short tap of the Right arrow, standing still. Returns radians per ms (signed).
double CalibrateCamera()
{
    double a0 = ConeAngle();
    Input.HoldArrow(Input.Right, 250);
    Thread.Sleep(150);
    double turned = Norm(ConeAngle() - a0);
    Input.HoldArrow(Input.Left, 250); // turn back
    Thread.Sleep(150);
    return Math.Abs(turned) < 0.01 ? 0 : turned / 250;
}

// Walks straight toward (tx, tz). Movement is camera-relative; the camera matrix at exe+1E5E528 gives c, s with
// W = (s, c) and D = (-c, s) in world (x, z) (measured in the Estersand 2026-11-13).
string WalkTo(double tx, double tz, double tol, double secs, bool stopOnMap)
{
    ushort map0 = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
    double fwd = 0;
    void Camera() { var m = Read(exe + 0x1E5E528, 0x24); fwd = Math.Atan2(BitConverter.ToSingle(m, 0), BitConverter.ToSingle(m, 0x20)); }
    Camera();
    var dirs = new (char[] k, double ang)[] { (['W'], 0), (['W', 'D'], 45), (['D'], 90), (['S', 'D'], 135), (['S'], 180), (['S', 'A'], -135), (['A'], -90), (['W', 'A'], -45) };
    var sw = System.Diagnostics.Stopwatch.StartNew(); int stuck = 0; double best = double.MaxValue; int noProgress = 0;
    while (true)
    {
        var c = Pos(); double dx = tx - c.x, dz = tz - c.z, d = Math.Sqrt(dx * dx + dz * dz);
        ushort map = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
        if (map != map0 && stopOnMap) return $"map changed {map0} -> {map} at {c.x:F2} {c.z:F2}";
        if (d <= tol) return $"arrived {c.x:F2} {c.y:F2} {c.z:F2} (off {d:F2})";
        if (sw.Elapsed.TotalSeconds > secs) return $"timeout at {c.x:F2} {c.y:F2} {c.z:F2}, {d:F2} away";
        if (d < best - 0.3) { best = d; noProgress = 0; } else noProgress++;
        double rel = Norm(Math.Atan2(dz, dx) - fwd) * 180 / Math.PI;
        var pick = dirs.OrderBy(o => Math.Abs(Norm((rel - o.ang) * Math.PI / 180))).First();
        if (noProgress > 6) { pick = dirs[(Array.IndexOf(dirs, pick) + (stuck % 2 == 0 ? 2 : 6)) % 8]; stuck++; noProgress = 0; best = d; }
        Input.Hold(pick.k, d < 4 ? 120 : 300); Thread.Sleep(60);
        Camera(); // the camera swings as the party walks
    }
}

static double Dist((float x, float y, float z) a, (float x, float y, float z) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
static double Norm(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

switch (args[0])
{
    case "maps":
        {
            // allocations below 4 GB of 16 MB+, by allocation base, with the mapped file name where there is one
            var sizes = new SortedDictionary<long, (long Size, uint Type)>();
            long a = 0;
            while (a < 0x100000000L && Native.VirtualQueryEx(h, (IntPtr)a, out var m, (IntPtr)Marshal.SizeOf<Native.MBI>()) != IntPtr.Zero)
            {
                if (m.State != 0x10000) { long ab = (long)m.AllocBase; sizes[ab] = sizes.TryGetValue(ab, out var o) ? (o.Size + (long)m.Size, o.Type) : ((long)m.Size, m.Type); }
                a = (long)m.Base + (long)m.Size;
            }
            foreach (var (b, v) in sizes)
            {
                if (v.Size < 0x1000000) continue;
                var sb = new System.Text.StringBuilder(512);
                Native.GetMappedFileNameW(h, (IntPtr)b, sb, 512);
                Console.WriteLine($"{b:X9} {v.Size >> 20,6} MB type {v.Type:X} {sb}");
            }
            break;
        }
    case "regions":
        var rs = Regions().ToList();
        Console.WriteLine($"{rs.Count} regions, {rs.Sum(r => r.Size) >> 20} MB");
        break;
    case "snap":
        using (var f = new BinaryWriter(File.Create(args[1])))
            foreach (var (b, s) in Regions())
            {
                var buf = Read(b, (int)s);
                f.Write(b); f.Write((int)s); f.Write(buf);
            }
        break;
    case "moved":
        {
            // Streams the three snapshots region by region (they hold the same regions in the same order when taken
            // seconds apart); regions that don't line up are skipped.
            var hits = new List<long>();
            using var ra = new BinaryReader(File.OpenRead(args[1]));
            using var rb = new BinaryReader(File.OpenRead(args[2]));
            using var rc = new BinaryReader(File.OpenRead(args[3]));
            var nb = new Dictionary<long, long>(); var nc = new Dictionary<long, long>();
            void Index(BinaryReader r, Dictionary<long, long> d) { while (r.BaseStream.Position < r.BaseStream.Length) { long pos = r.BaseStream.Position; long bs = r.ReadInt64(); int sz = r.ReadInt32(); d[bs] = pos; r.BaseStream.Seek(sz, SeekOrigin.Current); } }
            Index(rb, nb); Index(rc, nc);
            byte[] At(BinaryReader r, long pos) { r.BaseStream.Position = pos; r.ReadInt64(); int sz = r.ReadInt32(); return r.ReadBytes(sz); }
            while (ra.BaseStream.Position < ra.BaseStream.Length)
            {
                long b = ra.ReadInt64(); int size = ra.ReadInt32(); var a = ra.ReadBytes(size);
                if (!nb.TryGetValue(b, out long pb) || !nc.TryGetValue(b, out long pc)) continue;
                var bb = At(rb, pb); var cc = At(rc, pc);
                if (bb.Length != a.Length || cc.Length != a.Length) continue;
                for (int i = 0; i + 4 <= a.Length; i += 4)
                {
                    float x = BitConverter.ToSingle(a, i), y = BitConverter.ToSingle(bb, i), z = BitConverter.ToSingle(cc, i);
                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || Math.Abs(x) > 3000 || Math.Abs(z) > 3000) continue;
                    float d1 = y - x, d2 = z - y;
                    if (Math.Abs(d1) < 0.3 || Math.Abs(d2) < 0.3 || Math.Sign(d1) != Math.Sign(d2) || Math.Abs(z - x) > 100) continue;
                    hits.Add(b + i);
                }
            }
            var set = hits.ToHashSet();
            // A position is x, y, z: x and z move, y (height) sits between them.
            var triples = hits.Where(a => set.Contains(a + 8)).ToList();
            Console.WriteLine($"{hits.Count} moving floats, {triples.Count} x/z pairs 8 apart");
            foreach (var t in triples.Take(40))
            {
                var bytes = Read(t, 12);
                Console.WriteLine($"{Name(t)}: {BitConverter.ToSingle(bytes, 0):F2} {BitConverter.ToSingle(bytes, 4):F2} {BitConverter.ToSingle(bytes, 8):F2}");
            }
            break;
        }
    case "turned":
        {
            // turned <a> <b> <c>: floats within +-7 that changed steadily a->b->c by 0.1..2.5 each step (camera angle
            // candidates, radians), plus unit-vector components; addresses in the exe image first.
            using var ra = new BinaryReader(File.OpenRead(args[1]));
            using var rb = new BinaryReader(File.OpenRead(args[2]));
            using var rc = new BinaryReader(File.OpenRead(args[3]));
            var nb = new Dictionary<long, long>(); var nc = new Dictionary<long, long>();
            void Index(BinaryReader r, Dictionary<long, long> d) { while (r.BaseStream.Position < r.BaseStream.Length) { long pos = r.BaseStream.Position; long bs = r.ReadInt64(); int sz = r.ReadInt32(); d[bs] = pos; r.BaseStream.Seek(sz, SeekOrigin.Current); } }
            Index(rb, nb); Index(rc, nc);
            byte[] At(BinaryReader r, long pos) { r.BaseStream.Position = pos; r.ReadInt64(); int sz = r.ReadInt32(); return r.ReadBytes(sz); }
            var hits = new List<(long a, float x, float y, float z)>();
            while (ra.BaseStream.Position < ra.BaseStream.Length)
            {
                long b = ra.ReadInt64(); int size = ra.ReadInt32(); var a = ra.ReadBytes(size);
                if (!nb.TryGetValue(b, out long pb) || !nc.TryGetValue(b, out long pc)) continue;
                var bb = At(rb, pb); var cc = At(rc, pc);
                if (bb.Length != a.Length || cc.Length != a.Length) continue;
                for (int i = 0; i + 4 <= a.Length; i += 4)
                {
                    float x = BitConverter.ToSingle(a, i), y = BitConverter.ToSingle(bb, i), z = BitConverter.ToSingle(cc, i);
                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || Math.Abs(x) > 7 || Math.Abs(y) > 7 || Math.Abs(z) > 7) continue;
                    float d1 = y - x, d2 = z - y;
                    if (Math.Abs(d1) < 0.1 || Math.Abs(d2) < 0.1 || Math.Abs(d1) > 2.5 || Math.Abs(d2) > 2.5 || Math.Sign(d1) != Math.Sign(d2)) continue;
                    hits.Add((b + i, x, y, z));
                }
            }
            Console.WriteLine($"{hits.Count} candidates");
            foreach (var t in hits.OrderBy(q => Name(q.a).StartsWith("exe+") ? 0 : 1).Take(int.Parse(args.Length > 4 ? args[4] : "40")))
                Console.WriteLine($"{Name(t.a)}: {t.x:F3} -> {t.y:F3} -> {t.z:F3} (now {BitConverter.ToSingle(Read(t.a, 4), 0):F3})");
            break;
        }
    case "falling":
        {
            // falling <a> <b> <c>: u32 values that dropped a > b > c (100..9999999) while the u32 after them stayed the same
            // and >= a: current HP next to max HP while something takes damage
            using var ra = new BinaryReader(File.OpenRead(args[1]));
            using var rb = new BinaryReader(File.OpenRead(args[2]));
            using var rc = new BinaryReader(File.OpenRead(args[3]));
            var nb = new Dictionary<long, long>(); var nc = new Dictionary<long, long>();
            void Index(BinaryReader r, Dictionary<long, long> d) { while (r.BaseStream.Position < r.BaseStream.Length) { long pos = r.BaseStream.Position; long bs = r.ReadInt64(); int sz = r.ReadInt32(); d[bs] = pos; r.BaseStream.Seek(sz, SeekOrigin.Current); } }
            Index(rb, nb); Index(rc, nc);
            byte[] At(BinaryReader r, long pos) { r.BaseStream.Position = pos; r.ReadInt64(); int sz = r.ReadInt32(); return r.ReadBytes(sz); }
            while (ra.BaseStream.Position < ra.BaseStream.Length)
            {
                long b = ra.ReadInt64(); int size = ra.ReadInt32(); var a = ra.ReadBytes(size);
                if (!nb.TryGetValue(b, out long pb) || !nc.TryGetValue(b, out long pc)) continue;
                var bb = At(rb, pb); var cc = At(rc, pc);
                if (bb.Length != a.Length || cc.Length != a.Length) continue;
                for (int i = 0; i + 8 <= a.Length; i += 4)
                {
                    uint x = BitConverter.ToUInt32(a, i), y = BitConverter.ToUInt32(bb, i), z = BitConverter.ToUInt32(cc, i);
                    uint m = BitConverter.ToUInt32(a, i + 4);
                    if (x > y && y > z && z >= 100 && x < 10_000_000 && m >= x && m == BitConverter.ToUInt32(bb, i + 4) && m == BitConverter.ToUInt32(cc, i + 4))
                        Console.WriteLine($"{Name(b + i)}: {x} {y} {z} / {m}");
                }
            }
            break;
        }
    case "dump":
        // dump <addr> <len hex> <file>
        File.WriteAllBytes(args[3], Read(Parse(args[1]), Convert.ToInt32(args[2], 16)));
        break;
    case "get":
        {
            long a = Parse(args[2]); int n = args.Length > 3 ? int.Parse(args[3]) : 1;
            for (int i = 0; i < n; i++)
                Console.WriteLine(args[1] switch
                {
                    "u8" => Read(a + i, 1)[0].ToString(),
                    "u16" => BitConverter.ToUInt16(Read(a + i * 2, 2)).ToString(),
                    "u32" => BitConverter.ToUInt32(Read(a + i * 4, 4)).ToString(),
                    _ => BitConverter.ToSingle(Read(a + i * 4, 4)).ToString("F3"),
                });
            break;
        }
    case "put":
        {
            long a = Parse(args[2]);
            byte[] v = args[1] switch
            {
                "u8" => [byte.Parse(args[3])],
                "u16" => BitConverter.GetBytes(ushort.Parse(args[3])),
                "u32" => BitConverter.GetBytes(uint.Parse(args[3])),
                _ => BitConverter.GetBytes(float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture)),
            };
            Native.WriteProcessMemory(h, (IntPtr)a, v, (IntPtr)v.Length, out _);
            break;
        }
    case "mmcell":
        {
            // mmcell x,y [x,y ...]: raw colour and final classification of minimap pixels
            using var bmp = Minimap.Capture(p.MainWindowHandle);
            var grid = ReadGrid(bmp);
            foreach (var a in args.Skip(1))
            {
                var xy = a.Split(',').Select(int.Parse).ToArray();
                var col = bmp.GetPixel(xy[0], xy[1]);
                Console.WriteLine($"({xy[0]},{xy[1]}) {col.R} {col.G} {col.B} raw={Minimap.Classify(col)} final={grid[xy[0], xy[1]]}");
            }
            break;
        }
    case "viz":
        {
            // viz: live window with the current map, the party, the pathfinder's route and the trip (see Viz.cs).
            // Runs until closed; start it before or during a trip.
            VizForm.Run(() => Pos(), () => BitConverter.ToUInt16(Read(exe + 0x20441F0, 2)), p.MainWindowHandle);
            break;
        }
    case "teleport":
        {
            // teleport <crystal number>: from this map's crystal to another (numbers: mapdata.cs crystals). Walks to the
            // crystal (beside its landing spot, against the landing direction), opens its menu with Space, and drives the
            // menu by reading it from memory: cursor exe+203F22C, "menu open" exe+203F238. The destination list shows one line
            // per touched crystal in crystal-number order; touched crystals are taken from the bitmask at exe+2A2E5F4
            // (bit n = crystal n; candidate, checked against the list's length).
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int target = int.Parse(args[1]);
            int mapId = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            var here = Ff12MapData.Crystals.On(mapId);
            var info = Ff12MapData.MapInfo.For(mapId);
            if (here is null || info is null) { Console.WriteLine($"teleport: map 0x{mapId:X} has no teleport crystal"); return 1; }
            var dest = Ff12MapData.Crystals.List.FirstOrDefault(c => c.Number == target);
            if (dest is null) { Console.WriteLine($"teleport: no crystal number {target}"); return 1; }
            double facing = info.Info.Entrances[here.Entrance].Facing;
            double cx = here.X - Math.Sin(facing) * 4, cz = here.Z - Math.Cos(facing) * 4; // the crystal
            byte U8(long a) => Read(a, 1)[0];
            bool MenuOpen() => U8(exe + 0x203F238) == 1;
            int Cursor() => U8(exe + 0x203F22C);
            void Key(char k) { Input.Hold([k], 120); Thread.Sleep(350); }
            // walk up to it (the walk stops within about 1.5 units, on the landing spot's side)
            using (var pr = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Format(ci, "tnav {0} {1} 60", cx, cz)) { UseShellExecute = false, RedirectStandardOutput = true })!)
            { Console.WriteLine("  " + pr.StandardOutput.ReadToEnd().Trim().Split('\n').Last()); pr.WaitForExit(); }
            Input.Focus(p.MainWindowHandle);
            for (int tries = 0; tries < 6 && !MenuOpen(); tries++)
            {
                Key(' '); Thread.Sleep(600);
                if (!MenuOpen()) { Steer(cx, cz, 250); var q = Pos(); Console.WriteLine($"  no menu yet; stepped to ({q.x:F2}, {q.z:F2})"); }
            }
            if (!MenuOpen()) { Console.WriteLine("teleport: the crystal menu didn't open"); return 1; }
            // Save / Teleport / Done: cursor to Teleport
            for (int i = 0; i < 4 && Cursor() != 1; i++) Key(Cursor() < 1 ? 'S' : 'W');
            Key(' '); Thread.Sleep(500);
            // destination list: touched crystals in number order
            uint touched = BitConverter.ToUInt32(Read(exe + 0x2A2E5F4, 4));
            if ((touched & (1u << target)) == 0) { Console.WriteLine($"teleport: crystal {target} hasn't been touched yet (touched bits {touched:X8})"); Key('S'); return 1; }
            int line = Enumerable.Range(0, target).Count(n => (touched & (1u << n)) != 0);
            for (int i = 0; i < 40 && Cursor() != line; i++) Key(Cursor() < line ? 'S' : 'W');
            Console.WriteLine($"teleport: crystal {target} ({Ff12MapData.GameFiles.PlaceName(dest.Map)}) is line {line} of the destination list");
            Key(' ');
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 20 && BitConverter.ToUInt16(Read(exe + 0x20441F0, 2)) == mapId) Thread.Sleep(250);
            int now = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            Console.WriteLine(now == dest.Map ? $"teleported to 0x{now:X} ({Ff12MapData.GameFiles.PlaceName(now)})" : $"teleport: ended on map 0x{now:X}, expected 0x{dest.Map:X}");
            return now == dest.Map ? 0 : 1;
        }
    case "trip":
        {
            // trip <goalMapHex> [x z] [maxMaps=12]: walks across maps along the fewest-maps route over the game's own exits
            // (read from each map script by tools/MapReader). On each map it walks to the exit line from the inside, then
            // pushes across it; in the goal map it walks to (x, z) when given.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int goalMap = Convert.ToInt32(args[1], 16);
            double? gx = args.Length > 3 ? double.Parse(args[2], ci) : null, gz = args.Length > 3 ? double.Parse(args[3], ci) : null;
            int maxMaps = args.Length > 4 ? int.Parse(args[4]) : 12;
            string Walk(double x, double z)
            {
                using var pr = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Format(ci, "tnav {0} {1} 240", x, z)) { UseShellExecute = false, RedirectStandardOutput = true })!;
                string last = "";
                while (pr.StandardOutput.ReadLine() is string line) { if (!line.StartsWith("drift") && !line.StartsWith("planned") && !line.StartsWith("  re-aligned")) Console.WriteLine("    " + line); last = line; }
                pr.WaitForExit();
                return last;
            }
            ushort MapNow() => BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            string NameOf(int id) => Ff12MapData.GameFiles.PlaceName(id);
            string goalLabel = NameOf(goalMap) + (gx is double glx && gz is double glz ? $" ({glx:F0}, {glz:F0})" : "");
            // Waits for a new map to be playable: the position must first land near one of the new map's entrances (right
            // after a jump it still reads the old map's spot), then stay put for half a second. Arrival scenes (a region's
            // name banner) also hold the party still; tnav waits those out by watching for the minimap.
            void WaitPlayable()
            {
                int newMap = MapNow();
                var ents = Ff12MapData.MapInfo.For(newMap)?.Info.Entrances.Where(e => e.X != 0 || e.Z != 0).ToList() ?? [];
                bool AtEntrance((float x, float y, float z) q) => ents.Count == 0 || ents.Any(e => Math.Abs(e.X - q.x) + Math.Abs(e.Z - q.z) < 12);
                var settle = System.Diagnostics.Stopwatch.StartNew(); var lastP = Pos(); int still = 0;
                while (settle.Elapsed.TotalSeconds < 30 && (still < 2 || !AtEntrance(lastP)))
                { Thread.Sleep(250); var np = Pos(); still = Math.Abs(np.x - lastP.x) + Math.Abs(np.z - lastP.z) < 0.01 ? still + 1 : 0; lastP = np; }
            }
            var visited = new List<(int id, string name)>();
            for (int hop = 0; hop <= maxMaps; hop++)
            {
                int mapId = MapNow();
                var (px, _, pz) = Pos();
                string name = NameOf(mapId);
                visited.Add((mapId, name));
                Console.WriteLine($"[{hop}] map 0x{mapId:X} ({name}) at ({px:F1}, {pz:F1})");
                if (mapId == goalMap)
                {
                    if (gx is not double tx || gz is not double tz) { Console.WriteLine("TRIP DONE"); LiveState.Trip(visited, visited.Count - 1, -1, null, goalLabel, "arrived"); return 0; }
                    LiveState.Trip(visited, visited.Count - 1, -1, (tx, tz), goalLabel, "walking to the goal");
                    var r = Walk(tx, tz); Console.WriteLine(r.StartsWith("arrived") ? "TRIP DONE" : "trip ended: " + r);
                    LiveState.Trip(visited, visited.Count - 1, -1, (tx, tz), goalLabel, r.StartsWith("arrived") ? "arrived" : "stopped: " + r);
                    return r.StartsWith("arrived") ? 0 : 1;
                }
                // fewest-hops route from here over each map's exits (read as the search reaches each map) and, unless
                // FF12_BOT_TELEPORT=0, teleports: from a map with a teleport crystal to any crystal the party has touched
                // (bitmask at exe+2A2E5F4, bit n = crystal n; one Teleport Stone each)
                bool teleports = Environment.GetEnvironmentVariable("FF12_BOT_TELEPORT") != "0";
                uint touched = BitConverter.ToUInt32(Read(exe + 0x2A2E5F4, 4));
                var prev = new Dictionary<int, (int map, Ff12MapData.Exit? exit, int crystal)> { [mapId] = (-1, null, -1) }; var q = new Queue<int>(); q.Enqueue(mapId);
                while (q.Count > 0 && !prev.ContainsKey(goalMap))
                {
                    int m = q.Dequeue();
                    foreach (var x in Ff12MapData.MapInfo.For(m)?.Info.Exits ?? [])
                        foreach (var (dest, _) in x.Leads) if (prev.TryAdd(dest, (m, x, -1))) q.Enqueue(dest);
                    if (teleports && Ff12MapData.Crystals.On(m) is not null)
                        foreach (var c in Ff12MapData.Crystals.List.Where(c => c.Map != m && (touched & (1u << c.Number)) != 0))
                            if (prev.TryAdd(c.Map, (m, null, c.Number))) q.Enqueue(c.Map);
                }
                if (!prev.ContainsKey(goalMap)) { Console.WriteLine($"trip ended: no route from 0x{mapId:X} to 0x{goalMap:X}"); return 1; }
                int nextMap = goalMap; while (prev[nextMap].map != mapId) nextMap = prev[nextMap].map;
                var rest = new List<(int id, string name)>();
                for (int m = goalMap; m != mapId; m = prev[m].map) rest.Insert(0, (m, NameOf(m)));
                if (prev[nextMap].crystal is int crystal and >= 0)
                {
                    // teleport hop: the teleport command walks to this map's crystal and drives its menu from memory
                    Console.WriteLine($"    teleporting to crystal {crystal} ({NameOf(nextMap)})");
                    LiveState.Trip([.. visited, .. rest], visited.Count - 1, nextMap, null, goalLabel, $"teleporting to {NameOf(nextMap)}");
                    using (var tp = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"teleport {crystal}") { UseShellExecute = false, RedirectStandardOutput = true })!)
                    { while (tp.StandardOutput.ReadLine() is string line) Console.WriteLine("    " + line); tp.WaitForExit(); }
                    if (MapNow() == mapId) { Console.WriteLine("trip ended: the teleport didn't happen"); return 1; }
                    WaitPlayable();
                    continue;
                }
                var ex = prev[nextMap].exit!;
                // outwards: from this map's entrance nearest the line (its inside) through the line's middle
                var here = Ff12MapData.MapInfo.For(mapId)!;
                var inside = here.Info.Entrances.Where(e => e.X != 0 || e.Z != 0).OrderBy(e => Math.Pow(e.X - ex.Mid.X, 2) + Math.Pow(e.Z - ex.Mid.Z, 2))
                    .Select(e => (e.X, e.Z)).DefaultIfEmpty((px, pz)).First();
                double dx = ex.Mid.X - inside.Item1, dz = ex.Mid.Z - inside.Item2, len = Math.Max(0.01, Math.Sqrt(dx * dx + dz * dz)); dx /= len; dz /= len;
                Console.WriteLine($"    heading for the exit to 0x{nextMap:X} ({NameOf(nextMap)}) at ({ex.Mid.X:F1}, {ex.Mid.Z:F1})");
                LiveState.Trip([.. visited, .. rest], visited.Count - 1, nextMap, (ex.Mid.X, ex.Mid.Z), goalLabel, $"heading for the exit to {NameOf(nextMap)}");
                // read the next map (package match, minimap pictures) while walking this one, so the change doesn't wait on it
                var prefetch = Task.Run(() => { if (Ff12MapData.MapInfo.For(nextMap)?.Package is { } np) for (int v = 0; v < np.VariantCount; v++) np.Minimap(v); });
                var res = Walk(ex.Mid.X - dx * 2.5, ex.Mid.Z - dz * 2.5);
                // cross the line: keep walking outwards (from the map's entrance by the line through the line's middle)
                var push = System.Diagnostics.Stopwatch.StartNew();
                while (MapNow() == mapId && push.Elapsed.TotalSeconds < 8) { var (qx, _, qz) = Pos(); Steer(qx + dx * 6, qz + dz * 6, 300); Thread.Sleep(50); }
                var waitSw = System.Diagnostics.Stopwatch.StartNew();
                while (waitSw.Elapsed.TotalSeconds < 30 && MapNow() == mapId) Thread.Sleep(500);
                if (MapNow() == mapId) { Console.WriteLine($"trip ended: the exit at ({ex.Mid.X:F1}, {ex.Mid.Z:F1}) did not lead out ({res})"); LiveState.Trip(visited, visited.Count - 1, nextMap, (ex.Mid.X, ex.Mid.Z), goalLabel, "stopped: the exit did not lead out"); return 1; }
                Console.WriteLine($"    map changed to 0x{MapNow():X}");
                WaitPlayable();
            }
            Console.WriteLine("trip ended: too many maps");
            return 1;
        }
    case "tnav":
        {
            // tnav <x> <z> [seconds]: plans the whole route on the map package's minimap picture (tools/MapReader: the
            // package and its world position come from the game's files) and steers along it; re-plans when it drifts off
            // the route. A map with several minimap variants (what the game shows changes as ways open) uses the variant
            // that best matches the live minimap.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double tx = double.Parse(args[1], ci), tz = double.Parse(args[2], ci), secs = args.Length > 3 ? double.Parse(args[3], ci) : 120;
            int mapId = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            var startup = System.Diagnostics.Stopwatch.StartNew();
            var info = Ff12MapData.MapInfo.For(mapId);
            if (info?.Package is not { VariantCount: > 0 } pkg) { Console.WriteLine($"no map package with a minimap picture for map 0x{mapId:X}"); return 1; }
            int ox = (int)Math.Round(-pkg.Origin.X * 4), oy = (int)Math.Round(-pkg.Origin.Z * 4);
            Input.Focus(p.MainWindowHandle);
            // one route-planning picture per minimap variant (variants differ exactly where the game opens or closes a way)
            var cands = Enumerable.Range(0, pkg.VariantCount).Select(v => new TexMap(pkg.Minimap(v), ox, oy)).ToList();
            // arrival scenes (a region's name banner) hide the minimap and hold the party still: wait until the live minimap
            // matches one of the variants before walking (up to 20 s, then go anyway)
            var scores = cands.Select(cm => Align(cm, 12).score).ToList();
            var arrival = System.Diagnostics.Stopwatch.StartNew();
            if (scores.Max() < 0.25) Console.WriteLine("waiting for the minimap (an arrival scene?)");
            while (scores.Max() < 0.25 && arrival.Elapsed.TotalSeconds < 20) { Thread.Sleep(500); scores = cands.Select(cm => Align(cm, 12).score).ToList(); }
            // the variant must have a route from the party to the target; among those, the best match to the live minimap
            TexMap tm = cands[0]; int variant = 0; double bestScore = -1; bool bestRoutes = false;
            var here = Pos();
            for (int v = 0; v < cands.Count; v++)
            {
                var from = cands[v].ToPx(here.x, here.z); var to = cands[v].ToPx(tx, tz);
                bool routes = cands[v].Path(((int)from.u, (int)from.v), ((int)to.u, (int)to.v)).Count > 0;
                if (routes && !bestRoutes || routes == bestRoutes && scores[v] > bestScore) { bestScore = scores[v]; tm = cands[v]; variant = v; bestRoutes = routes; }
            }
            if (!bestRoutes) Console.WriteLine($"no minimap variant of {pkg.Name} has a route from here to ({tx:F1}, {tz:F1})");
            Console.WriteLine($"map 0x{mapId:X} ({info.Place}) = {pkg.Name} variant {variant} of {pkg.VariantCount}, offset ({ox}, {oy}), aligned score {bestScore:F2}, ready in {startup.ElapsedMilliseconds} ms");
            // why the party is stuck at a spot, from the map's own data (barriers, floor that needs another variant, triggers)
            void Explain(double x, double z) { foreach (var line in info.Explain(new(x, z))) Console.WriteLine("  why: " + line); }
            var alignTimer = System.Diagnostics.Stopwatch.StartNew();
            Input.Focus(p.MainWindowHandle);
            var navDir = Paths.Out("game");
            // smooth steering (hold the movement keys, turn the camera); FF12_BOT_STEER=keys uses the older key taps
            bool smooth = Environment.GetEnvironmentVariable("FF12_BOT_STEER") != "keys";
            // the camera's turn rate: from the previous walk (the Helm keeps refining it) when that was recent, else measured once
            var rateFile = Path.Combine(Paths.Out("live"), "camera.txt");
            double camRate = !smooth ? 0 : File.Exists(rateFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(rateFile) < TimeSpan.FromMinutes(10)
                && double.TryParse(File.ReadAllText(rateFile), System.Globalization.NumberStyles.Float, ci, out double saved) && saved != 0 ? saved : CalibrateCamera();
            if (smooth) Console.WriteLine($"camera turns {camRate * 1000 * 180 / Math.PI:F0} degrees per second of Right arrow");
            var helm = new Helm(ConeAngle, camRate);
            // hand the learned rate on to the next walk (the next map of a trip)
            using var saveRate = new OnExit(() => { if (smooth && helm.Rate != 0) File.WriteAllText(rateFile, helm.Rate.ToString("R", ci)); });
            using var keys = Input.Session();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gpx = tm.ToPx(tx, tz);
            List<(int x, int y)> route = new(); int idx = 0, steps = 0;
            // for the live viewer (memtool viz): the route in world units, the goal and the walls found on this walk
            var blockedAt = new List<(double x, double z)>();
            void Live(string status) => LiveState.Route(mapId, variant, route.Select(r => tm.ToWorld(r.x, r.y)), (tx, tz), blockedAt, status);
            var lastCheck = Pos(); var checkTimer = System.Diagnostics.Stopwatch.StartNew(); int stuckTries = 0; double bestGoal = double.MaxValue;
            while (sw.Elapsed.TotalSeconds < secs)
            {
                var c = Pos();
                // the game lost focus (someone alt-tabbed): keys would go to the other window, so let go and wait for it
                if (!Input.IsFocused(p.MainWindowHandle))
                {
                    Input.ReleaseAll(); Console.WriteLine("paused: the game isn't the focused window");
                    while (!Input.IsFocused(p.MainWindowHandle) && !p.HasExited) Thread.Sleep(250);
                    Console.WriteLine("resumed"); checkTimer.Restart(); lastCheck = Pos(); continue;
                }
                if (BitConverter.ToUInt16(Read(exe + 0x20441F0, 2)) != mapId) { Console.WriteLine($"map changed at {c.x:F2} {c.z:F2}"); Live("map changed"); return 0; }
                double toGoal = Math.Sqrt((tx - c.x) * (tx - c.x) + (tz - c.z) * (tz - c.z));
                if (toGoal < 1.5) { Console.WriteLine($"arrived {c.x:F2} {c.z:F2}"); Live("arrived"); return 0; }
                var at = tm.ToPx(c.x, c.z);
                // nearest route point; re-plan if none is within 6 px (off the route) or there is no route yet
                int near = -1; double nd = double.MaxValue;
                for (int i = Math.Max(0, idx - 5); i < route.Count; i++)
                {
                    double d = Math.Sqrt((route[i].x - at.u) * (route[i].x - at.u) + (route[i].y - at.v) * (route[i].y - at.v));
                    if (d < nd) { nd = d; near = i; }
                }
                if (route.Count == 0 || nd > 6)
                {
                    route = tm.Smooth(tm.Path(((int)at.u, (int)at.v), ((int)gpx.u, (int)gpx.v)));
                    if (route.Count == 0) { Console.WriteLine($"no route on the map texture from {c.x:F2} {c.z:F2}"); return 1; }
                    idx = 0; near = 0;
                    Console.WriteLine($"planned {route.Count} px from ({c.x:F1}, {c.z:F1})");
                    tm.SaveDebug(Path.Combine(navDir, "tnav.png"), route, at, route[Math.Min(route.Count - 1, 9)]);
                    Live(blockedAt.Count > 0 ? "walking around a wall" : "walking");
                }
                idx = near;
                // aim farther ahead when steering smoothly (24 px = 6 units): a near target swings around as the party moves
                int ahead = smooth && camRate != 0 ? 24 : 9;
                bool last = idx + ahead >= route.Count - 1;
                var next = route[Math.Min(route.Count - 1, idx + ahead)];
                var (nx, nz) = last ? (tx, tz) : tm.ToWorld(next.x, next.y);
                if (smooth && camRate != 0) { helm.Update(Math.Atan2(nz - c.z, nx - c.x)); Thread.Sleep(30); } else Steer(nx, nz, toGoal < 4 ? 120 : 250);
                if (alignTimer.Elapsed.TotalSeconds >= 3) { var a = Align(tm, 6); if (a.dx != 0 || a.dy != 0) { Console.WriteLine($"drift corrected by ({a.dx}, {a.dy}), score {a.score:F2}"); route.Clear(); } alignTimer.Restart(); }
                steps++;
                if (checkTimer.Elapsed.TotalSeconds >= 5)
                {
                    var now = Pos(); double moved = Math.Sqrt((now.x - lastCheck.x) * (now.x - lastCheck.x) + (now.z - lastCheck.z) * (now.z - lastCheck.z));
                    double tg = Math.Sqrt((tx - now.x) * (tx - now.x) + (tz - now.z) * (tz - now.z));
                    if (moved < 1)
                    {
                        if (++stuckTries > 4) { Console.WriteLine($"stuck at {now.x:F2} {now.z:F2}: moved {moved:F2} units in 5 s, {stuckTries - 1} blocks added did not help"); Explain(now.x, now.z); Live("stuck"); return 1; }
                        // a wall the texture doesn't show: block the corridor just ahead, plan around it (this walk only: barriers change with the story)
                        var atp = tm.ToPx(now.x, now.z); var np = tm.ToPx(nx, nz);
                        double ax = np.u - atp.u, az = np.v - atp.v, len = Math.Max(0.01, Math.Sqrt(ax * ax + az * az));
                        double bu = atp.u + ax / len * 9, bv = atp.v + az / len * 9;
                        tm.AddBlock(bu, bv, 8);
                        blockedAt.Add(tm.ToWorld(bu, bv));
                        Console.WriteLine($"blocked at {now.x:F2} {now.z:F2}: wall ahead at world ({(bu - tm.Ox) / 4:F1}, {(bv - tm.Oy) / 4:F1}) added, re-planning");
                        Explain(now.x, now.z);
                        Input.ReleaseAll(); Steer(now.x - ax / len * 2, now.z - az / len * 2, 500); // back off from the wall
                        route.Clear();
                    }
                    if (moved >= 1 && tg < bestGoal - 2) { stuckTries = 0; bestGoal = tg; }
                    lastCheck = Pos(); checkTimer.Restart();
                }
            }
            Console.WriteLine($"timeout at {Pos().x:F2} {Pos().z:F2}");
            return 1;
        }
    case "minimap":
        {
            Input.Focus(p.MainWindowHandle);
            using var bmp = Minimap.Capture(p.MainWindowHandle);
            var grid = ReadGrid(bmp);
            Console.WriteLine($"marker {Minimap.Marker(grid)} {Minimap.LastInfo}");
            var mk = Minimap.Marker(grid) ?? (135, 138);
            Minimap.SaveDebug(Path.Combine(Paths.Out("game"), "mm.png"), bmp, grid, new() { ((int)mk.x, (int)mk.y) }, ((int)mk.x, (int)mk.y), mk);
            bmp.Save(Path.Combine(Paths.Out("game"), "mmraw.png"));
            var corpus = Paths.Out("minimaps");
            bmp.Save(Path.Combine(corpus, $"m{BitConverter.ToUInt16(Read(exe + 0x20441F0, 2))}_{DateTime.Now:HHmmss}_a{ConeAngle().ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}.png"));
            break;
        }
    case "where":
        {
            var (x, y, z) = Pos();
            var cm = Read(exe + 0x1E5E528, 0x24); float cc = BitConverter.ToSingle(cm, 0), ss = BitConverter.ToSingle(cm, 0x20);
            var o = Pos0();
            Console.WriteLine($"map {BitConverter.ToUInt16(Read(exe + 0x20441F0, 2))} story {BitConverter.ToUInt16(Read(exe + 0x2044480, 2))} pos {x:F2} {y:F2} {z:F2} W->({ss:F2},{cc:F2}) D->({-cc:F2},{ss:F2}) old {o.x:F2} {o.y:F2} {o.z:F2}");
            break;
        }
    case "goto":
        {
            // goto <x> <z> [tolerance] [seconds] [stopOnMapChange 1|0]: walks straight toward (x, z) with WASD.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Input.Focus(p.MainWindowHandle);
            var r = WalkTo(double.Parse(args[1], ci), double.Parse(args[2], ci), args.Length > 3 ? double.Parse(args[3], ci) : 1.5,
                args.Length > 4 ? double.Parse(args[4], ci) : 60, args.Length <= 5 || args[5] != "0");
            Console.WriteLine(r);
            return r.StartsWith("timeout") ? 1 : 0;
        }
    case "mmcal":
        {
            // mmcal: walks forward briefly and compares minimap shifts with the world move -> pixels per unit and axes.
            Input.Focus(p.MainWindowHandle);
            var a0 = Pos(); Cell0();
            Thread.Sleep(200);
            using (var b0 = Minimap.Capture(p.MainWindowHandle))
            {
                var g0 = Minimap.Grid(b0);
                Input.Hold(['W'], 900); Thread.Sleep(400);
                var a1 = Pos();
                using var b1 = Minimap.Capture(p.MainWindowHandle);
                var g1 = Minimap.Grid(b1);
                var e0 = Minimap.EdgeMap(b0); var e1 = Minimap.EdgeMap(b1); int dx = 0, dy = 0; double bestc = -1;
                for (int sy = -60; sy <= 60; sy++) for (int sx = -60; sx <= 60; sx++)
                {
                    double c = 0;
                    for (int y = 30; y < Minimap.Size - 30; y += 2) for (int x = 30; x < Minimap.Size - 30; x += 2)
                    { int ax = x + sx, ay = y + sy; if (ax >= 0 && ay >= 0 && ax < Minimap.Size && ay < Minimap.Size && (x - 135) * (x - 135) + (y - 138) * (y - 138) > 400) c += e1[x, y] * e0[ax, ay]; }
                    if (c > bestc) { bestc = c; dx = sx; dy = sy; }
                }
                double wx = a1.x - a0.x, wz = a1.z - a0.z;
                // the map scrolls opposite to the party: a feature at pixel u in g1 was at u + shift in g0
                Console.WriteLine($"world move ({wx:F2}, {wz:F2}) minimap shift ({dx}, {dy}) -> px/unit x {(wx == 0 ? 0 : dx / wx):F2} z {(wz == 0 ? 0 : dy / wz):F2}");
            }
            break;
        }
    case "nav":
        {
            // nav <x> <z> [pxPerUnit] [seconds]: plans over the minimap's walkable area and walks the route in legs.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double tx = double.Parse(args[1], ci), tz = double.Parse(args[2], ci);
            double k = args.Length > 3 ? double.Parse(args[3], ci) : 4.6, secs = args.Length > 4 ? double.Parse(args[4], ci) : 120;
            Input.Focus(p.MainWindowHandle);
            ushort map0 = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var navDir = Paths.Out("game");
            var logDir = Path.Combine(navDir, "navlog"); if (Directory.Exists(logDir)) Directory.Delete(logDir, true); Directory.CreateDirectory(logDir); int legNo = 0;
            var lastCheck = Pos(); var checkTimer = System.Diagnostics.Stopwatch.StartNew(); int stuckTries = 0; double bestGoal = double.MaxValue;
            while (sw.Elapsed.TotalSeconds < secs)
            {
                var c = Pos();
                if (Math.Sqrt((tx - c.x) * (tx - c.x) + (tz - c.z) * (tz - c.z)) < 2) { Console.WriteLine($"arrived {c.x:F2} {c.z:F2}"); return 0; }
                if (BitConverter.ToUInt16(Read(exe + 0x20441F0, 2)) != map0) { Console.WriteLine($"map changed at {c.x:F2} {c.z:F2}"); return 0; }
                using var bmp = Minimap.Capture(p.MainWindowHandle);
                var grid = ReadGrid(bmp);
                var m = Minimap.Marker(grid) ?? (135, 138);
                var start = ((int)Math.Round(m.x), (int)Math.Round(m.y));
                // north-up minimap: +x right, +z down (from mmcal)
                var goal = (m.x + (tx - c.x) * k, m.y + (tz - c.z) * k);
                var path = Minimap.Path(grid, start, goal);
                double GoalGap(List<(int x, int y)> pp) { var e = pp[^1]; return Math.Sqrt((e.x - goal.Item1) * (e.x - goal.Item1) + (e.y - goal.Item2) * (e.y - goal.Item2)); }
                var narrow = Minimap.Path(grid, start, goal, 1); // narrow passages: allow hugging walls when that gets much closer
                if (GoalGap(narrow) < GoalGap(path) - 8) path = narrow;
                if (path.Count < 6) { var e = path[^1]; Console.WriteLine($"closest reachable point on the minimap reached at {c.x:F2} {c.z:F2}; target is {Math.Sqrt((e.x - goal.Item1) * (e.x - goal.Item1) + (e.y - goal.Item2) * (e.y - goal.Item2)) / k:F1} units further (wall or off the floor)"); return 1; }
                // follow the route closely: steer one short step toward a point a little way along it, then re-plan
                var leg = path[Math.Min(path.Count - 1, 10)];
                double wx = c.x + (leg.x - m.x) / k, wz = c.z + (leg.y - m.y) / k;
                if (legNo % 6 == 0)
                {
                    Console.WriteLine($"step {legNo}: at ({c.x:F1}, {c.z:F1}), route {path.Count} px");
                    Minimap.SaveDebug(Path.Combine(navDir, "nav.png"), bmp, grid, path, leg, goal);
                    File.Copy(Path.Combine(navDir, "nav.png"), Path.Combine(logDir, $"leg_{legNo / 6 + 1:D2}.png"), true);
                }
                legNo++;
                Steer(wx, wz, 250);
                // stuck check: running covers several units in 5 s; under 1 unit means something blocks the way
                if (checkTimer.Elapsed.TotalSeconds >= 5)
                {
                    var now = Pos(); double moved = Math.Sqrt((now.x - lastCheck.x) * (now.x - lastCheck.x) + (now.z - lastCheck.z) * (now.z - lastCheck.z));
                    if (moved < 1)
                    {
                        if (++stuckTries > 2) { Console.WriteLine($"stuck at {now.x:F2} {now.z:F2}: moved {moved:F2} units in 5 s, {stuckTries} sidesteps did not help"); return 1; }
                        Console.WriteLine($"stuck at {now.x:F2} {now.z:F2} (moved {moved:F2} in 5 s), sidestepping");
                        // step sideways to the route direction, alternating sides
                        double ax = wx - now.x, az = wz - now.z, len = Math.Max(0.01, Math.Sqrt(ax * ax + az * az)), side = stuckTries % 2 == 1 ? 1 : -1;
                        Steer(now.x - az / len * 3 * side, now.z + ax / len * 3 * side, 700);
                    }
                    double toGoal = Math.Sqrt((tx - now.x) * (tx - now.x) + (tz - now.z) * (tz - now.z));
                    if (moved >= 1 && toGoal < bestGoal - 2) { stuckTries = 0; bestGoal = toGoal; } // real progress, not just sidestepping
                    lastCheck = Pos(); checkTimer.Restart();
                }
            }
            Console.WriteLine($"timeout at {Pos().x:F2} {Pos().z:F2}");
            return 1;
        }
    case "setpos":
        {
            // setpos <addr> <x> <y> <z>: writes three floats (a position)
            long a = Parse(args[1]);
            for (int i = 0; i < 3; i++)
            {
                var v = BitConverter.GetBytes(float.Parse(args[2 + i], System.Globalization.CultureInfo.InvariantCulture));
                Native.WriteProcessMemory(h, (IntPtr)(a + i * 4), v, (IntPtr)4, out _);
            }
            break;
        }
    case "vscan":
        {
            // vscan start [width=1|2|4] | eq <n> | ne <n> | changed | same | up | down | list : a value-diff scanner over
            // the game's own data (the exe image, where the story flag, map id, camera and party live). Do something in
            // the game between steps (open a menu, move its cursor) and keep the addresses that behaved the same way.
            // State in work/bot/vscan (the previous image and the surviving offsets).
            var dir = Paths.Out("vscan");
            string snapFile = Path.Combine(dir, "snap.bin"), candFile = Path.Combine(dir, "cand.bin"), widthFile = Path.Combine(dir, "width.txt");
            int size = p.MainModule!.ModuleMemorySize;
            var now = Read(exe, size);
            if (args[1] == "start")
            {
                int w = args.Length > 2 ? int.Parse(args[2]) : 1;
                File.WriteAllText(widthFile, w.ToString());
                File.WriteAllBytes(snapFile, now);
                if (File.Exists(candFile)) File.Delete(candFile);
                Console.WriteLine($"recorded {size / 1024} KB, width {w}");
                break;
            }
            int width = int.Parse(File.ReadAllText(widthFile));
            var before = File.ReadAllBytes(snapFile);
            long V(byte[] b, int o) => width == 1 ? b[o] : width == 2 ? BitConverter.ToInt16(b, o) : BitConverter.ToInt32(b, o);
            IEnumerable<int> cands = Enumerable.Range(0, (size - width) / width).Select(i => i * width);
            if (File.Exists(candFile)) { var cb = File.ReadAllBytes(candFile); cands = Enumerable.Range(0, cb.Length / 4).Select(i => BitConverter.ToInt32(cb, i * 4)); }
            long n = args.Length > 2 ? long.Parse(args[2]) : 0;
            Func<int, bool> keep = args[1] switch
            {
                "eq" => o => V(now, o) == n,
                "ne" => o => V(now, o) != n,
                "changed" => o => V(now, o) != V(before, o),
                "same" => o => V(now, o) == V(before, o),
                "up" => o => V(now, o) > V(before, o),
                "down" => o => V(now, o) < V(before, o),
                "list" => _ => true,
                _ => throw new ArgumentException("start | eq n | ne n | changed | same | up | down | list"),
            };
            var left = cands.Where(o => o + width <= now.Length && keep(o)).ToList();
            using (var bw = new BinaryWriter(File.Create(candFile))) foreach (var o in left) bw.Write(o);
            File.WriteAllBytes(snapFile, now);
            Console.WriteLine($"{left.Count} addresses left");
            foreach (var o in left.Take(args[1] == "list" ? int.MaxValue : 25)) Console.WriteLine($"  exe+{o:X}: {V(before, o)} -> {V(now, o)}");
            break;
        }
    case "posscan":
        {
            // posscan start|next: finds where the controlled character's position lives. "start" saves every float triple
            // (x, y, z) in writable memory with x and z in 0..1000 and |y| < 200; "next" (after walking a bit) keeps the
            // ones whose x/z moved 0.3..30 units since the last scan with y staying within 5. Candidates: posscan.bin.
            var file = Path.Combine(Path.GetTempPath(), "posscan.bin");
            var keep = new List<(long a, float x, float y, float z)>();
            bool ok(float x, float y, float z) => float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && x > 0.5 && x < 1000 && z > 0.5 && z < 1000 && Math.Abs(y) < 200;
            if (args[1] == "start")
            {
                foreach (var (b, s) in Regions())
                {
                    var buf = Read(b, (int)s);
                    for (int i = 0; i + 12 <= buf.Length; i += 4)
                    { float x = BitConverter.ToSingle(buf, i), y = BitConverter.ToSingle(buf, i + 4), z = BitConverter.ToSingle(buf, i + 8); if (ok(x, y, z)) keep.Add((b + i, x, y, z)); }
                }
            }
            else
            {
                using (var br = new BinaryReader(File.OpenRead(file)))
                    while (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        long a = br.ReadInt64(); float x0 = br.ReadSingle(), y0 = br.ReadSingle(), z0 = br.ReadSingle();
                        var v = Read(a, 12); float x = BitConverter.ToSingle(v, 0), y = BitConverter.ToSingle(v, 4), z = BitConverter.ToSingle(v, 8);
                        double d = Math.Sqrt((x - x0) * (x - x0) + (z - z0) * (z - z0));
                        if (ok(x, y, z) && (args[1] == "still" ? d < 0.001 && Math.Abs(y - y0) < 0.001 : d > 0.3 && d < 30 && Math.Abs(y - y0) < 5)) keep.Add((a, x, y, z));
                    }
                foreach (var k in keep.OrderBy(k => Name(k.a).StartsWith("exe+") ? 0 : 1).Take(30)) Console.WriteLine($"{Name(k.a)} {k.x:F2} {k.y:F2} {k.z:F2}");
            }
            using (var bw = new BinaryWriter(File.Create(file)))
                foreach (var k in keep) { bw.Write(k.a); bw.Write(k.x); bw.Write(k.y); bw.Write(k.z); }
            Console.WriteLine($"{keep.Count} candidates");
            break;
        }
    case "findpair":
        {
            // findpair <a> <b> [width 2|4]: addresses where value a is followed by value b (e.g. current and max HP)
            uint va = uint.Parse(args[1]), vb = uint.Parse(args[2]); int w = args.Length > 3 ? int.Parse(args[3]) : 4;
            foreach (var (b, s) in Regions())
            {
                var buf = Read(b, (int)s);
                for (int i = 0; i + 2 * w <= buf.Length; i += w)
                {
                    uint x = w == 4 ? BitConverter.ToUInt32(buf, i) : BitConverter.ToUInt16(buf, i);
                    uint y = w == 4 ? BitConverter.ToUInt32(buf, i + w) : BitConverter.ToUInt16(buf, i + w);
                    if (x == va && y == vb) Console.WriteLine(Name(b + i));
                }
            }
            break;
        }
    case "find":
        {
            byte[] v = args[1] == "f32" ? BitConverter.GetBytes(float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)) : BitConverter.GetBytes(uint.Parse(args[2]));
            foreach (var (b, s) in Regions())
            {
                var buf = Read(b, (int)s);
                for (int i = 0; i + 4 <= buf.Length; i += 4)
                    if (buf.AsSpan(i, 4).SequenceEqual(v)) Console.WriteLine(Name(b + i));
            }
            break;
        }
}
return 0;

static Dictionary<long, byte[]> Load(string file)
{
    var d = new Dictionary<long, byte[]>();
    using var r = new BinaryReader(File.OpenRead(file));
    while (r.BaseStream.Position < r.BaseStream.Length) { long b = r.ReadInt64(); int s = r.ReadInt32(); d[b] = r.ReadBytes(s); }
    return d;
}

static class Input
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }

    // Windows only lets a background process take focus right after input, so tap Alt first.
    public static void Focus(IntPtr h)
    {
        if (GetForegroundWindow() == h) return;
        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(h); Thread.Sleep(400);
    }

    static void Send(char key, bool up)
    {
        var i = new INPUT[1]; i[0].type = 1; i[0].ki.scan = (ushort)MapVirtualKey(key, 0); i[0].ki.flags = 8u | (up ? 2u : 0u);
        SendInput(1, i, Marshal.SizeOf<INPUT>());
    }

    // holds the keys together (scan codes, as the game reads them)
    public static void Hold(char[] keys, int ms)
    {
        foreach (var k in keys) Send(k, false);
        Thread.Sleep(ms);
        foreach (var k in keys) Send(k, true);
    }

    public static bool IsFocused(IntPtr h) => GetForegroundWindow() == h;

    public const ushort Left = 0x25, Right = 0x27;

    // an arrow key (an extended key: it needs the extended flag to reach the game as an arrow)
    public static void HoldArrow(ushort vk, int ms)
    {
        void SendArrow(bool up)
        {
            var i = new INPUT[1]; i[0].type = 1; i[0].ki.scan = (ushort)MapVirtualKey(vk, 0); i[0].ki.flags = 8u | 1u | (up ? 2u : 0u);
            SendInput(1, i, Marshal.SizeOf<INPUT>());
        }
        SendArrow(false); Thread.Sleep(ms); SendArrow(true);
    }

    static readonly HashSet<char> held = new();

    /// <summary>Keeps exactly these movement keys down: presses the new ones, releases the rest, so running never pauses
    /// between steering updates.</summary>
    public static void Keep(char[] keys)
    {
        foreach (var k in held.Except(keys).ToList()) { Send(k, true); held.Remove(k); }
        foreach (var k in keys.Except(held).ToList()) { Send(k, false); held.Add(k); }
    }

    public static void ReleaseAll() => Keep([]);

    /// <summary>Releases every held movement key when the scope ends (including early returns).</summary>
    public static IDisposable Session() => new Releaser();
    sealed class Releaser : IDisposable { public void Dispose() => ReleaseAll(); }
}

static class Native
{
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, IntPtr n, out IntPtr r);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, IntPtr n, out IntPtr r);
    [DllImport("psapi.dll", CharSet = CharSet.Unicode)] public static extern uint GetMappedFileNameW(IntPtr h, IntPtr a, System.Text.StringBuilder s, uint n);
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr a, out MBI m, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct MBI { public IntPtr Base, AllocBase; public uint AllocProtect; public IntPtr Size; public uint State, Protect, Type; }
}

/// <summary>Runs an action when the scope ends, early returns included.</summary>
sealed class OnExit(Action action) : IDisposable { public void Dispose() => action(); }
