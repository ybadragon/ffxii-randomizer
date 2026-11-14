using System.Diagnostics;
using System.Runtime.InteropServices;

// memtool: reads (and on request writes) FFXII_TZA.exe memory for testing.
//  regions                         writable memory summary
//  snap <file>                     dumps all writable private memory
//  moved <a> <b> <c>               floats that moved steadily a->b->c (player position candidates), as xyz triples first
//  get <type> <addr> [n]           type u8/u16/u32/f32; addr hex or exe+hex
//  put <type> <addr> <value>
//  find <type> <value>             addresses holding value (writable memory)
if (args[0] == "fitroute")
{
    // fitroute <texture.png> x,z x,z ...: finds scale and offset (pixel = world*s + o) putting the walked points on the
    // texture's red walkable band; prints the best few.
    using var tex = new System.Drawing.Bitmap(args[1]);
    int w = tex.Width, hh = tex.Height;
    var red = new bool[w, hh];
    for (int y = 0; y < hh; y++) for (int x = 0; x < w; x++) { var c = tex.GetPixel(x, y); red[x, y] = c.A > 100 && c.R > 150 && c.G < 120; }
    var pts = args.Skip(2).Select(a => a.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList();
    var res = new List<(int score, double s, int ox, int oy)>();
    for (double s = 0.8; s <= 5.0; s += 0.02)
    {
        int best = -1, bx = 0, by = 0;
        for (int oy = -(int)(400 * s); oy < hh; oy += 2)
            for (int ox = -(int)(400 * s); ox < w; ox += 2)
            {
                int sc = 0;
                foreach (var pt in pts) { int u = (int)(pt[0] * s) + ox, v = (int)(pt[1] * s) + oy; if (u >= 0 && v >= 0 && u < w && v < hh && red[u, v]) sc++; }
                if (sc > best) { best = sc; bx = ox; by = oy; }
            }
        res.Add((best, s, bx, by));
    }
    foreach (var r in res.OrderByDescending(r => r.score).ThenByDescending(r => r.s).Take(8)) Console.WriteLine($"{r.score}/{pts.Count} at scale {r.s:F2} offset ({r.ox}, {r.oy})");
    return 0;
}
if (args[0] == "tim2")
{
    // tim2 <file> <outPrefix>: finds every TIM2 image in a file (e.g. menumap_subNN.bin) and writes it as PNG.
    // TIM2 layout: "TIM2" + version + format + count (16 bytes); per picture a header: totalSize u32, clutSize u32,
    // imageSize u32, headerSize u16, clutColors u16, picFormat u8, mipmaps u8, clutType u8, imageType u8,
    // width u16, height u16, then GsTex0/1, flags, then image data and the CLUT. imageType: 1=16bpp 2=24 3=32 4=4bit 5=8bit.
    var bytes = File.ReadAllBytes(args[1]);
    int found = 0;
    for (int at = 0; at + 64 < bytes.Length; at++)
    {
        if (bytes[at] != 'T' || bytes[at + 1] != 'I' || bytes[at + 2] != 'M' || bytes[at + 3] != '2') continue;
        int ph = at + 16;
        uint total = BitConverter.ToUInt32(bytes, ph), clutSize = BitConverter.ToUInt32(bytes, ph + 4), imgSize = BitConverter.ToUInt32(bytes, ph + 8);
        int hdr = BitConverter.ToUInt16(bytes, ph + 12), clutColors = BitConverter.ToUInt16(bytes, ph + 14);
        int clutType = bytes[ph + 18], imgType = bytes[ph + 19];
        int w = BitConverter.ToUInt16(bytes, ph + 20), ht = BitConverter.ToUInt16(bytes, ph + 22);
        int img = ph + hdr, clut = img + (int)imgSize;
        Console.WriteLine($"TIM2 at 0x{at:X}: {w}x{ht} type {imgType} clutType 0x{clutType:X} clutColors {clutColors} imgSize {imgSize} total {total}");
        if (w == 0 || ht == 0 || img + imgSize > bytes.Length) { Console.WriteLine("  (runs past the end of the file, skipped)"); continue; }
        if (clut + clutColors * 4 > bytes.Length) { Console.WriteLine("  (palette cut short by the end of the file; missing entries shown magenta)"); Array.Resize(ref bytes, clut + clutColors * 4); }
        System.Drawing.Color Clut16or32(int i)
        {
            if ((clutType & 0x3) == 3) { int o = clut + i * 4; return System.Drawing.Color.FromArgb(Math.Min(255, bytes[o + 3] * 2), bytes[o], bytes[o + 1], bytes[o + 2]); }
            if ((clutType & 0x3) == 2) { int o = clut + i * 3; return System.Drawing.Color.FromArgb(255, bytes[o], bytes[o + 1], bytes[o + 2]); }
            int v = BitConverter.ToUInt16(bytes, clut + i * 2);
            return System.Drawing.Color.FromArgb((v & 0x8000) != 0 ? 255 : 0, (v & 31) * 8, ((v >> 5) & 31) * 8, ((v >> 10) & 31) * 8);
        }
        // PS2 8-bit CLUTs are stored in a swizzled ("CSM1") order unless the clut type says otherwise
        int UnswizzleIndex(int i) => (clutType & 0x80) != 0 ? i : (i & ~0x18) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);
        using var bmp = new System.Drawing.Bitmap(w, ht, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < ht; y++)
            for (int x = 0; x < w; x++)
            {
                System.Drawing.Color c;
                int i = y * w + x;
                switch (imgType)
                {
                    case 5: c = Clut16or32(clutColors == 256 ? UnswizzleIndex(bytes[img + i]) : bytes[img + i]); break;
                    case 4: { int b = bytes[img + i / 2]; c = Clut16or32((i & 1) == 0 ? b & 15 : b >> 4); break; }
                    case 3: { int o = img + i * 4; c = System.Drawing.Color.FromArgb(Math.Min(255, bytes[o + 3] * 2), bytes[o], bytes[o + 1], bytes[o + 2]); break; }
                    case 2: { int o = img + i * 3; c = System.Drawing.Color.FromArgb(255, bytes[o], bytes[o + 1], bytes[o + 2]); break; }
                    default: { int v = BitConverter.ToUInt16(bytes, img + i * 2); c = System.Drawing.Color.FromArgb(255, (v & 31) * 8, ((v >> 5) & 31) * 8, ((v >> 10) & 31) * 8); break; }
                }
                bmp.SetPixel(x, y, c);
            }
        bmp.Save($"{args[2]}_{found++:D2}.png");
        at += 15;
    }
    return 0;
}
if (args[0] == "mapfit")
{
    // mapfit <menumap_subNN.bin> <picture prefix>: for every decoded picture (prefix_NN.png) and every LCMPOS1 block,
    // finds the offset that puts the most of the block's world points on floor at 4 px per world unit.
    var bytes = File.ReadAllBytes(args[1]);
    int blocks = BitConverter.ToInt32(bytes, 0x10);
    var blockPts = new List<List<(float x, float z)>>();
    for (int bi = 0; bi < blocks; bi++)
    {
        int start = 0x20 + BitConverter.ToUInt16(bytes, 0x14 + bi * 2);
        int end = bi + 1 < blocks ? 0x20 + BitConverter.ToUInt16(bytes, 0x14 + (bi + 1) * 2) : start + 0x600;
        var pts = new List<(float, float)>();
        for (int o = start; o + 16 <= Math.Min(end, bytes.Length); o += 16)
        {
            float x = BitConverter.ToSingle(bytes, o), y = BitConverter.ToSingle(bytes, o + 4), z = BitConverter.ToSingle(bytes, o + 8);
            if (x > 1 && x < 3000 && z > 1 && z < 3000 && y > -60 && y < 300 && float.IsFinite(x)) pts.Add((x, z));
        }
        blockPts.Add(pts);
    }
    for (int pi = 0; File.Exists($"{args[2]}_{pi:D2}.png"); pi++)
    {
        using var pic = new System.Drawing.Bitmap($"{args[2]}_{pi:D2}.png");
        int w = pic.Width, hh = pic.Height;
        var bg = pic.GetPixel(2, 2);
        var floor = new bool[w, hh];
        for (int y = 0; y < hh; y++) for (int x = 0; x < w; x++)
        { var c = pic.GetPixel(x, y); floor[x, y] = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) + Math.Abs(c.A - bg.A) > 40; }
        // distance-tolerant floor: within 4 px of floor counts (exit lines sit on corridor ends)
        var near = new bool[w, hh];
        for (int y = 0; y < hh; y++) for (int x = 0; x < w; x++)
            if (floor[x, y]) for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
            { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < w && ay < hh) near[ax, ay] = true; }
        for (int bi = 0; bi < blocks; bi++)
        {
            var pts = blockPts[bi]; if (pts.Count == 0) continue;
            int best = -1, bx = 0, by = 0;
            float minx = pts.Min(p => p.x), maxx = pts.Max(p => p.x), minz = pts.Min(p => p.z), maxz = pts.Max(p => p.z);
            for (int oy = (int)(-maxz * 4); oy <= hh - (int)(minz * 4); oy++)
                for (int ox = (int)(-maxx * 4); ox <= w - (int)(minx * 4); ox++)
                {
                    int score = 0;
                    foreach (var (x, z) in pts)
                    {
                        int u = (int)(x * 4) + ox, v = (int)(z * 4) + oy;
                        if (u >= 0 && v >= 0 && u < w && v < hh && near[u, v]) score++;
                    }
                    if (score > best) { best = score; bx = ox; by = oy; }
                }
            Console.WriteLine($"picture {pi} ({w}x{hh}) block {bi}: {best}/{pts.Count} points on floor at offset ({bx}, {by})");
        }
    }
    return 0;
}
if (args[0] == "mmfile")
{
    // mmfile <capture.png>...: reads saved minimap captures offline (no game needed). The camera angle is taken from the
    // file name (..._a<radians>.png) when present. Writes <capture>.read.png next to each: capture | reading.
    foreach (var file in args.Skip(1))
    {
        using var shot = new System.Drawing.Bitmap(file);
        var am = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(file), @"_a(-?\d+(\.\d+)?)$");
        double? ang = am.Success ? double.Parse(am.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        var g = Environment.GetEnvironmentVariable("MMMODE") == "edge" ? Minimap.EdgeGrid(shot, ang) : Minimap.WatershedGrid(shot, ang);
        var mk = Minimap.Marker(g) ?? (135, 138);
        int floor = 0; foreach (var c in g) if (c == Minimap.Cell.Floor) floor++;
        Minimap.SaveDebug(Path.ChangeExtension(file, ".read.png"), shot, g, new() { ((int)mk.x, (int)mk.y) }, ((int)mk.x, (int)mk.y), mk);
        Console.WriteLine($"{Path.GetFileName(file)}: angle {ang?.ToString("F2") ?? "?"}, floor {floor} px");
    }
    return 0;
}
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
    case "trip":
        {
            // trip <goalMapHex> [x z] [maxMaps=12]: walks across maps along the fewest-maps route over the game's own exits
            // (tools/mapdata/exits.csv, read from each map script's trigger lines and the mapjumps they run). On each map it walks
            // to the exit line, from the inside, then pushes across it; in the goal map it walks to (x, z) when given.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int goalMap = Convert.ToInt32(args[1], 16);
            double? gx = args.Length > 3 ? double.Parse(args[2], ci) : null, gz = args.Length > 3 ? double.Parse(args[3], ci) : null;
            int maxMaps = args.Length > 4 ? int.Parse(args[4]) : 12;
            var exitRows = File.ReadAllLines(Paths.Exits).Skip(1).Select(l => l.Split(','))
                .Select(f => (map: Convert.ToInt32(f[0], 16), name: f[1], mx: double.Parse(f[3], ci), mz: double.Parse(f[4], ci),
                              ix: double.Parse(f[5], ci), iz: double.Parse(f[6], ci), dest: Convert.ToInt32(f[7], 16), dname: f[8], ent: int.Parse(f[9]))).ToList();
            string Walk(double x, double z)
            {
                using var pr = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Format(ci, "tnav {0} {1} 240", x, z)) { UseShellExecute = false, RedirectStandardOutput = true })!;
                string last = "";
                while (pr.StandardOutput.ReadLine() is string line) { if (!line.StartsWith("drift") && !line.StartsWith("planned") && !line.StartsWith("  re-aligned")) Console.WriteLine("    " + line); last = line; }
                pr.WaitForExit();
                return last;
            }
            ushort MapNow() => BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            for (int hop = 0; hop <= maxMaps; hop++)
            {
                int mapId = MapNow();
                var (px, _, pz) = Pos();
                string name = exitRows.FirstOrDefault(r => r.map == mapId).name ?? "?";
                Console.WriteLine($"[{hop}] map 0x{mapId:X} ({name}) at ({px:F1}, {pz:F1})");
                if (mapId == goalMap)
                {
                    if (gx is not double tx || gz is not double tz) { Console.WriteLine("TRIP DONE"); return 0; }
                    var r = Walk(tx, tz); Console.WriteLine(r.StartsWith("arrived") ? "TRIP DONE" : "trip ended: " + r); return r.StartsWith("arrived") ? 0 : 1;
                }
                // fewest-maps route from here
                var prev = new Dictionary<int, int> { [mapId] = -1 }; var q = new Queue<int>(); q.Enqueue(mapId);
                while (q.Count > 0 && !prev.ContainsKey(goalMap))
                { int m = q.Dequeue(); foreach (var r in exitRows.Where(r => r.map == m)) if (prev.TryAdd(r.dest, m)) q.Enqueue(r.dest); }
                if (!prev.ContainsKey(goalMap)) { Console.WriteLine($"trip ended: no route from 0x{mapId:X} to 0x{goalMap:X}"); return 1; }
                int nextMap = goalMap; while (prev[nextMap] != mapId) nextMap = prev[nextMap];
                var ex = exitRows.First(r => r.map == mapId && r.dest == nextMap);
                double dx = ex.mx - ex.ix, dz = ex.mz - ex.iz, len = Math.Max(0.01, Math.Sqrt(dx * dx + dz * dz)); dx /= len; dz /= len;
                Console.WriteLine($"    heading for the exit to 0x{nextMap:X} ({ex.dname}) at ({ex.mx:F1}, {ex.mz:F1})");
                var res = Walk(ex.mx - dx * 2.5, ex.mz - dz * 2.5);
                // cross the line: keep walking outwards (from the map's entrance by the line through the line's middle)
                var push = System.Diagnostics.Stopwatch.StartNew();
                while (MapNow() == mapId && push.Elapsed.TotalSeconds < 8) { var (qx, _, qz) = Pos(); Steer(qx + dx * 6, qz + dz * 6, 300); Thread.Sleep(50); }
                var waitSw = System.Diagnostics.Stopwatch.StartNew();
                while (waitSw.Elapsed.TotalSeconds < 30 && MapNow() == mapId) Thread.Sleep(500);
                if (MapNow() == mapId) { Console.WriteLine($"trip ended: the exit at ({ex.mx:F1}, {ex.mz:F1}) did not lead out ({res})"); return 1; }
                Console.WriteLine($"    map changed to 0x{MapNow():X}");
                // wait for the next map to finish loading: the party position settles near the entrance
                var settle = System.Diagnostics.Stopwatch.StartNew(); var lastP = Pos(); int still = 0;
                while (settle.Elapsed.TotalSeconds < 20 && still < 4) { Thread.Sleep(500); var np = Pos(); still = Math.Abs(np.x - lastP.x) + Math.Abs(np.z - lastP.z) < 0.01 ? still + 1 : 0; lastP = np; }
                Thread.Sleep(1500);
            }
            Console.WriteLine("trip ended: too many maps");
            return 1;
        }
    case "tnav":
        {
            // tnav <x> <z> [seconds]: plans the whole route on the map package's minimap texture (calibrated with mapfind,
            // cached in texdir/maps.csv) and steers along it; re-plans when it drifts off the route.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double tx = double.Parse(args[1], ci), tz = double.Parse(args[2], ci), secs = args.Length > 3 ? double.Parse(args[3], ci) : 120;
            string texdir = Paths.Tex;
            int mapId = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            string? Cached() => File.Exists(Path.Combine(texdir, "maps.csv")) ? File.ReadAllLines(Path.Combine(texdir, "maps.csv")).FirstOrDefault(l => l.StartsWith(mapId + ",")) : null;
            if (Cached() is null)
            {
                Console.WriteLine($"map {mapId} not calibrated yet, running mapfind");
                using var pr = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"mapfind \"{texdir}\"") { UseShellExecute = false })!;
                pr.WaitForExit();
            }
            var f = Cached()?.Split(',');
            if (f is null || double.Parse(f[4], ci) < 0.5) { Console.WriteLine($"no reliable texture for map {mapId}: {string.Join(",", f ?? [])}"); return 1; }
            var tm = new TexMap(Path.Combine(texdir, f[1] + ".png"), int.Parse(f[2]), int.Parse(f[3]));
            Console.WriteLine($"map {mapId} = {f[1]} offset ({f[2]}, {f[3]})");
            Input.Focus(p.MainWindowHandle);
            { var a0 = Align(tm, 12); Console.WriteLine($"aligned by ({a0.dx}, {a0.dy}), score {a0.score:F2}"); }
            var alignTimer = System.Diagnostics.Stopwatch.StartNew();
            Input.Focus(p.MainWindowHandle);
            var navDir = Paths.Out("game");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gpx = tm.ToPx(tx, tz);
            List<(int x, int y)> route = new(); int idx = 0, steps = 0;
            var lastCheck = Pos(); var checkTimer = System.Diagnostics.Stopwatch.StartNew(); int stuckTries = 0; double bestGoal = double.MaxValue;
            while (sw.Elapsed.TotalSeconds < secs)
            {
                var c = Pos();
                if (BitConverter.ToUInt16(Read(exe + 0x20441F0, 2)) != mapId) { Console.WriteLine($"map changed at {c.x:F2} {c.z:F2}"); return 0; }
                double toGoal = Math.Sqrt((tx - c.x) * (tx - c.x) + (tz - c.z) * (tz - c.z));
                if (toGoal < 1.5) { Console.WriteLine($"arrived {c.x:F2} {c.z:F2}"); return 0; }
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
                    route = tm.Path(((int)at.u, (int)at.v), ((int)gpx.u, (int)gpx.v));
                    if (route.Count == 0) { Console.WriteLine($"no route on the map texture from {c.x:F2} {c.z:F2}"); return 1; }
                    idx = 0; near = 0;
                    Console.WriteLine($"planned {route.Count} px from ({c.x:F1}, {c.z:F1})");
                    tm.SaveDebug(Path.Combine(navDir, "tnav.png"), route, at, route[Math.Min(route.Count - 1, 9)]);
                }
                idx = near;
                bool last = idx + 9 >= route.Count - 1;
                var next = route[Math.Min(route.Count - 1, idx + 9)];
                var (nx, nz) = last ? (tx, tz) : tm.ToWorld(next.x, next.y);
                Steer(nx, nz, toGoal < 4 ? 120 : 250);
                if (alignTimer.Elapsed.TotalSeconds >= 3) { var a = Align(tm, 6); if (a.dx != 0 || a.dy != 0) { Console.WriteLine($"drift corrected by ({a.dx}, {a.dy}), score {a.score:F2}"); route.Clear(); } alignTimer.Restart(); }
                steps++;
                if (checkTimer.Elapsed.TotalSeconds >= 5)
                {
                    var now = Pos(); double moved = Math.Sqrt((now.x - lastCheck.x) * (now.x - lastCheck.x) + (now.z - lastCheck.z) * (now.z - lastCheck.z));
                    double tg = Math.Sqrt((tx - now.x) * (tx - now.x) + (tz - now.z) * (tz - now.z));
                    if (moved < 1)
                    {
                        if (++stuckTries > 4) { Console.WriteLine($"stuck at {now.x:F2} {now.z:F2}: moved {moved:F2} units in 5 s, {stuckTries - 1} blocks added did not help"); return 1; }
                        // a wall the texture doesn't show: block the corridor just ahead, plan around it (this walk only: barriers change with the story)
                        var atp = tm.ToPx(now.x, now.z); var np = tm.ToPx(nx, nz);
                        double ax = np.u - atp.u, az = np.v - atp.v, len = Math.Max(0.01, Math.Sqrt(ax * ax + az * az));
                        double bu = atp.u + ax / len * 9, bv = atp.v + az / len * 9;
                        tm.AddBlock(bu, bv, 8);
                        Console.WriteLine($"blocked at {now.x:F2} {now.z:F2}: wall ahead at world ({(bu - tm.Ox) / 4:F1}, {(bv - tm.Oy) / 4:F1}) added, re-planning");
                        Steer(now.x - ax / len * 2, now.z - az / len * 2, 500); // back off from the wall
                        route.Clear();
                    }
                    if (moved >= 1 && tg < bestGoal - 2) { stuckTries = 0; bestGoal = tg; }
                    lastCheck = Pos(); checkTimer.Restart();
                }
            }
            Console.WriteLine($"timeout at {Pos().x:F2} {Pos().z:F2}");
            return 1;
        }
    case "mapfind":
        {
            // mapfind <texdir> [mmScale=4.63]: finds which map package texture is the current map and its offset
            // (texture px = world * 4 + offset), by matching the live minimap's wall lines against each texture of the
            // same area. Caches the answer in <texdir>/maps.csv (mapId,package,ox,oy,score).
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string dir = args[1]; double mmScale = args.Length > 2 ? double.Parse(args[2], ci) : 4.63;
            int mapId = BitConverter.ToUInt16(Read(exe + 0x20441F0, 2));
            var areaCsv = File.ReadAllLines(Paths.MapAreas);
            string? script = areaCsv.Select(l => l.Split(',')).Where(f => f.Length >= 2 && int.TryParse(f[0], System.Globalization.NumberStyles.HexNumber, ci, out int id) && id == mapId).Select(f => f[1]).FirstOrDefault();
            if (script is null) { Console.WriteLine($"map {mapId} not in mapAreas.csv"); return 1; }
            string prefix = script[..3];
            var cands = Directory.GetFiles(dir, prefix + "*.png").OrderBy(f => f).ToList();
            Console.WriteLine($"map {mapId} = {script}; {cands.Count} candidate textures for '{prefix}'");
            Input.Focus(p.MainWindowHandle);
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
            var coarse = top.Where((_, i) => i % 4 == 0).ToList();
            // Exact calibration from the packages' NPS2 records: take the package whose world box contains the party with
            // floor under it; offset = -origin * 4. Image matching below is only the fallback.
            {
                (string pkg, int ox, int oy, int floor)? pick = null;
                foreach (var file in cands)
                {
                    string pkg0 = Path.GetFileNameWithoutExtension(file);
                    if (Nps2.Get(dir, pkg0) is not { } r) continue;
                    if (wx < r.x0 - 3 || wz < r.z0 - 3 || wx > r.x0 + r.w + 3 || wz > r.z0 + r.h + 3) continue;
                    int ox = (int)Math.Round(-r.x0 * 4), oy = (int)Math.Round(-r.z0 * 4);
                    var tm0 = new TexMap(file, ox, oy);
                    var (u0, v0) = tm0.ToPx(wx, wz); int fl = 0;
                    for (int dy = -12; dy <= 12; dy++) for (int dx = -12; dx <= 12; dx++)
                    { int a = (int)u0 + dx, c = (int)v0 + dy; if (a >= 0 && c >= 0 && a < tm0.W && c < tm0.H && tm0.Floor[a, c]) fl++; }
                    // The map's own entrances and exit lines (from its script, tools/mapdata/exits.csv) must lie on the package's floor:
                    // several packages can cover the party's spot (seen: dst_a02 has dst0102 and dst0107), only one has them all.
                    int exPts = 0, onFloor = 0;
                    var exitsCsv = Paths.Exits;
                    if (File.Exists(exitsCsv))
                        foreach (var ef in File.ReadLines(exitsCsv).Skip(1).Select(l => l.Split(',')).Where(f => Convert.ToInt32(f[0], 16) == mapId))
                            for (int k = 3; k <= 5; k += 2)
                            {
                                var (eu, ev) = tm0.ToPx(double.Parse(ef[k], System.Globalization.CultureInfo.InvariantCulture), double.Parse(ef[k + 1], System.Globalization.CultureInfo.InvariantCulture));
                                exPts++; bool hit = false;
                                for (int dy = -10; dy <= 10 && !hit; dy++) for (int dx = -10; dx <= 10 && !hit; dx++)
                                { int a = (int)eu + dx, c = (int)ev + dy; if (a >= 0 && c >= 0 && a < tm0.W && c < tm0.H && tm0.Floor[a, c]) hit = true; }
                                if (hit) onFloor++;
                            }
                    Console.WriteLine($"  {pkg0}: box ({r.x0:F1},{r.z0:F1}) {r.w}x{r.h} contains the party, {fl} floor px around it, {onFloor}/{exPts} entrance and exit points on its floor");
                    int score = onFloor * 100000 + fl;
                    if (fl > 0 && (pick is null || score > pick.Value.floor)) pick = (pkg0, ox, oy, score);
                }
                if (pick is { } pk)
                {
                    Console.WriteLine($"best: map {mapId} = {pk.pkg} offset ({pk.ox}, {pk.oy}) from its NPS2 record");
                    var cache0 = Path.Combine(dir, "maps.csv");
                    var lines0 = File.Exists(cache0) ? File.ReadAllLines(cache0).Where(l => !l.StartsWith(mapId + ",")).ToList() : new List<string>();
                    lines0.Add($"{mapId},{pk.pkg},{pk.ox},{pk.oy},1.000");
                    File.WriteAllLines(cache0, lines0);
                    break;
                }
                Console.WriteLine("  no package record contains the party; falling back to image matching");
            }
            (string file, double score, int ox, int oy) win = ("", -1, 0, 0);
            foreach (var file in cands)
            {
                using var tex = new System.Drawing.Bitmap(file);
                var te = Minimap.EdgeMap(tex);
                int w = tex.Width, hh = tex.Height;
                double Score(List<(double dx, double dy, float g)> set, int cx, int cy)
                {
                    double s = 0;
                    foreach (var t in set) { int u = cx + (int)t.dx, v = cy + (int)t.dy; if (u >= 0 && v >= 0 && u < w && v < hh) s += te[u, v]; }
                    return s / set.Count;
                }
                // Vaan's texture position (cx, cy) anywhere on the texture; coarse 3 px grid, then refine
                double best = -1; int bx = 0, by = 0;
                for (int cy = 0; cy < hh; cy += 3) for (int cx = 0; cx < w; cx += 3) { double s = Score(coarse, cx, cy); if (s > best) { best = s; bx = cx; by = cy; } }
                for (int cy = by - 4; cy <= by + 4; cy++) for (int cx = bx - 4; cx <= bx + 4; cx++) { double s = Score(top, cx, cy); if (s > best || (cx == bx - 4 && cy == by - 4)) { if (s > best) { best = s; bx = cx; by = cy; } } }
                best = Score(top, bx, by);
                int ox = bx - (int)Math.Round(wx * 4), oy = by - (int)Math.Round(wz * 4);
                Console.WriteLine($"  {Path.GetFileNameWithoutExtension(file)}: {best:F3} offset ({ox}, {oy})");
                if (best > win.score) win = (file, best, ox, oy);
            }
            string pkg = Path.GetFileNameWithoutExtension(win.file);
            Console.WriteLine($"best: map {mapId} = {pkg} offset ({win.ox}, {win.oy}) score {win.score:F3}");
            var cache = Path.Combine(dir, "maps.csv");
            var lines = File.Exists(cache) ? File.ReadAllLines(cache).Where(l => !l.StartsWith(mapId + ",")).ToList() : new List<string>();
            lines.Add($"{mapId},{pkg},{win.ox},{win.oy},{win.score.ToString("F3", ci)}");
            File.WriteAllLines(cache, lines);
            break;
        }
    case "texcal":
        {
            // texcal <texture.png> [ox0 oy0 radius]: matches the live minimap's wall lines against a map package's minimap
            // texture over scale and offset (texture px = world * s + o); minimap = 4.63 px/unit around the marker.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Input.Focus(p.MainWindowHandle);
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
                    pts.Add((dx / 4.63, dy / 4.63, me[x, y]));
                }
            var top = pts.OrderByDescending(t => t.g).Take(1200).ToList();
            using var tex = new System.Drawing.Bitmap(args[1]);
            var te = Minimap.EdgeMap(tex);
            int w = tex.Width, hh = tex.Height;
            int ox0 = args.Length > 3 ? int.Parse(args[2]) : -463, oy0 = args.Length > 3 ? int.Parse(args[3]) : -515, rad = args.Length > 4 ? int.Parse(args[4]) : 60;
            double best = -1, bs = 0; int bx = 0, by = 0; var perScale = new SortedDictionary<double, double>();
            for (double s = 3.80; s <= 4.601; s += 0.01)
                for (int oy = oy0 - rad; oy <= oy0 + rad; oy++)
                    for (int ox = ox0 - rad; ox <= ox0 + rad; ox++)
                    {
                        double cx = wx * s + ox, cy = wz * s + oy, sc = 0;
                        foreach (var t in top)
                        {
                            int u = (int)(cx + t.dx * s), v = (int)(cy + t.dy * s);
                            if (u >= 0 && v >= 0 && u < w && v < hh) sc += te[u, v];
                        }
                        if (sc > best) { best = sc; bs = s; bx = ox; by = oy; }
                        if (sc > perScale.GetValueOrDefault(Math.Round(s, 2))) perScale[Math.Round(s, 2)] = sc;
                    }
            foreach (var (k, v) in perScale) if (Math.Abs(k * 100 % 5) < 0.01) Console.Write($"{k:F2}:{v / top.Count:F3}  ");
            Console.WriteLine();
            Console.WriteLine($"best scale {bs:F2} offset ({bx}, {by}) score {best / top.Count:F3} (Vaan at texture {wx * bs + bx:F0},{wz * bs + by:F0})");
            break;
        }
    case "scalescan":
        {
            // scalescan <texture.png> [mmScale=4.9]: best match score of the live minimap against the texture for texture
            // scales 1.5..8 px per world unit (coarse 4 px placement grid).
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double mmScale = args.Length > 2 ? double.Parse(args[2], ci) : 4.9;
            Input.Focus(p.MainWindowHandle);
            using var mm = Minimap.Capture(p.MainWindowHandle);
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
                    pts.Add((dx / mmScale, dy / mmScale, me[x, y]));
                }
            var top = pts.OrderByDescending(t => t.g).Take(400).ToList();
            using var tex = new System.Drawing.Bitmap(args[1]);
            var te = Minimap.EdgeMap(tex); int w = tex.Width, hh = tex.Height;
            for (double s = 1.5; s <= 8.01; s += 0.25)
            {
                double best = -1; int bx = 0, by = 0;
                for (int cy = 0; cy < hh; cy += 4) for (int cx = 0; cx < w; cx += 4)
                {
                    double sc = 0;
                    foreach (var t in top) { int u = cx + (int)(t.dx * s), v = cy + (int)(t.dy * s); if (u >= 0 && v >= 0 && u < w && v < hh) sc += te[u, v]; }
                    if (sc > best) { best = sc; bx = cx; by = cy; }
                }
                Console.WriteLine($"scale {s:F2}: {best / top.Count:F3} at ({bx},{by})");
            }
            break;
        }
    case "mapcal":
        {
            // mapcal <picture prefix> [mmScale=4.63]: aligns the live minimap with each of an area's clean map pictures
            // (prefix_NN.png, 4 px per world unit) by matching wall edges; prints the best picture and its offset
            // (picture pixel = world * 4 + offset).
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double mmScale = args.Length > 2 ? double.Parse(args[2], ci) : 4.63;
            Input.Focus(p.MainWindowHandle);
            using var mm = Minimap.Capture(p.MainWindowHandle);
            var (wx, _, wz) = Pos();
            var g0 = Minimap.Grid(mm); var mk = Minimap.Marker(g0) ?? (135, 138);
            var mg = Minimap.EdgeMap(mm);
            double ca = ConeAngle();
            var pts = new List<(int x, int y, double g)>();
            for (int y = 24; y < Minimap.Size - 24; y++)
                for (int x = 24; x < Minimap.Size - 24; x++)
                {
                    double dx = x - mk.x, dy = y - mk.y, dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 16) continue;
                    double off = Math.Abs(Math.IEEERemainder(Math.Atan2(dy, dx) - ca, 2 * Math.PI));
                    if (dist < 110 && Math.Abs(off - Minimap.ConeHalfAngle) < 0.12) continue;
                    pts.Add((x, y, mg[x, y]));
                }
            var top = pts.OrderByDescending(t => t.g).Take(1500).Select(t => ((t.x - mk.x) * 4 / mmScale, (t.y - mk.y) * 4 / mmScale)).ToList();
            // Shape matching: sample the minimap's floor (+1) and outside (-1) pixels; the score of a placement is how
            // much floor lands on picture floor minus how much outside does.
            var gm = Minimap.Grid(mm, ca);
            var shape = new List<(double dx, double dy, int wgt)>();
            for (int y = 26; y < Minimap.Size - 26; y += 2)
                for (int x = 26; x < Minimap.Size - 26; x += 2)
                {
                    if ((x - mk.x) * (x - mk.x) + (y - mk.y) * (y - mk.y) < 196) continue;
                    int wgt = gm[x, y] == Minimap.Cell.Floor ? 1 : gm[x, y] == Minimap.Cell.Outside ? -1 : 0;
                    if (wgt != 0) shape.Add(((x - mk.x) * 4 / mmScale, (y - mk.y) * 4 / mmScale, wgt));
                }
            var results = new List<(string file, double score, int ox, int oy)>();
            for (int pi = 0; File.Exists($"{args[1]}_{pi:D2}.png"); pi++)
            {
                using var pic = new System.Drawing.Bitmap($"{args[1]}_{pi:D2}.png");
                var pg = Minimap.FloorMask(pic);
                int w = pic.Width, hh = pic.Height;
                double Score(int px0, int py0)
                {
                    double s = 0;
                    foreach (var (ddx, ddy, wgt) in shape)
                    {
                        int u = px0 + (int)Math.Round(ddx), v = py0 + (int)Math.Round(ddy);
                        if (u >= 0 && v >= 0 && u < w && v < hh) s += wgt * pg[u, v]; else s -= wgt > 0 ? 1 : 0;
                    }
                    return s / shape.Count;
                }
                double best = -1; int bx = 0, by = 0;
                for (int py0 = 0; py0 < hh; py0 += 3) for (int px0 = 0; px0 < w; px0 += 3) { double s = Score(px0, py0); if (s > best) { best = s; bx = px0; by = py0; } }
                for (int py0 = by - 3; py0 <= by + 3; py0++) for (int px0 = bx - 3; px0 <= bx + 3; px0++) { double s = Score(px0, py0); if (s > best) { best = s; bx = px0; by = py0; } }
                int ox = bx - (int)Math.Round(wx * 4), oy = by - (int)Math.Round(wz * 4);
                results.Add(($"{args[1]}_{pi:D2}.png", best, ox, oy));
                Console.WriteLine($"picture {pi} ({w}x{hh}): match {best:F3}, Vaan at picture ({bx},{by}), offset ({ox}, {oy})");
            }
            var win = results.OrderByDescending(r => r.score).First();
            Console.WriteLine($"best: {Path.GetFileName(win.file)} offset ({win.ox}, {win.oy}) for map {BitConverter.ToUInt16(Read(exe + 0x20441F0, 2))}");
            break;
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
    case "godmode":
        {
            // godmode [seconds=3600]: test-only; keeps every party member at full HP (current = max in both party HP
            // tables found at exe+1F7B088 and exe+21A80A0, 0xC0 bytes per member) so walking tests can't be wiped.
            double secs = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3600;
            var sw = System.Diagnostics.Stopwatch.StartNew(); int refills = 0;
            while (sw.Elapsed.TotalSeconds < secs && !p.HasExited)
            {
                foreach (long tbl in new long[] { exe + 0x1F7B088, exe + 0x21A80A0 })
                    for (int i = 0; i < 6; i++)
                    {
                        var b = Read(tbl + i * 0xC0, 8);
                        uint cur = BitConverter.ToUInt32(b, 0), max = BitConverter.ToUInt32(b, 4);
                        if (max > 0 && max < 100000 && cur < max) { Native.WriteProcessMemory(h, (IntPtr)(tbl + i * 0xC0), BitConverter.GetBytes(max), (IntPtr)4, out _); refills++; }
                    }
                Thread.Sleep(200);
            }
            Console.WriteLine($"godmode ended, {refills} refills");
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
