// dumpinfo <file.dmp>: exception, modules and a raw stack scan (return-address candidates) from a minidump.
using var f = File.OpenRead(args[0]);
using var r = new BinaryReader(f);
uint U32(long at) { f.Position = at; return r.ReadUInt32(); }
ulong U64(long at) { f.Position = at; return r.ReadUInt64(); }
string Str(long rva) { uint n = U32(rva); f.Position = rva + 4; return System.Text.Encoding.Unicode.GetString(r.ReadBytes((int)n)); }

uint streams = U32(8), dir = U32(12);
var loc = new Dictionary<uint, (uint Size, uint Rva)>();
for (int i = 0; i < streams; i++) { long e = dir + i * 12; loc[U32(e)] = (U32(e + 4), U32(e + 8)); }

var mods = new List<(ulong Base, uint Size, string Name)>();
if (loc.TryGetValue(4, out var ml))
{
    uint n = U32(ml.Rva);
    for (int i = 0; i < n; i++) { long m = ml.Rva + 4 + i * 108; mods.Add((U64(m), U32(m + 8), Path.GetFileName(Str(U32(m + 20))))); }
}
string Sym(ulong a) { foreach (var m in mods) if (a >= m.Base && a < m.Base + m.Size) return $"{m.Name}+{a - m.Base:X}"; return ""; }

// full-memory map for reading process memory (Memory64ListStream = 9)
var mem = new List<(ulong Start, ulong Size, long FileAt)>();
if (loc.TryGetValue(9, out var m64))
{
    ulong count = U64(m64.Rva); long at = (long)U64(m64.Rva + 8);
    for (ulong i = 0; i < count; i++) { long d = m64.Rva + 16 + (long)i * 16; ulong s = U64(d), z = U64(d + 8); mem.Add((s, z, at)); at += (long)z; }
}
byte[]? ReadMem(ulong a, int n) { foreach (var m in mem) if (a >= m.Start && a + (ulong)n <= m.Start + m.Size) { f.Position = m.FileAt + (long)(a - m.Start); return r.ReadBytes(n); } return null; }

if (args.Length > 1 && args[1] == "low" && loc.TryGetValue(16, out var mi))
{
    // MemoryInfoList: allocations below 4 GB, merged by allocation base
    uint hs = U32(mi.Rva), es = U32(mi.Rva + 4); ulong cnt = U64(mi.Rva + 8);
    var allocs = new SortedDictionary<ulong, (ulong Size, uint State, uint Type)>();
    for (ulong i = 0; i < cnt; i++)
    {
        long e = mi.Rva + hs + (long)i * es;
        ulong b = U64(e), ab = U64(e + 8), sz = U64(e + 24); uint st = U32(e + 32), ty = U32(e + 40);
        if (b >= 0x100000000UL || st == 0x10000) continue;
        allocs[ab] = allocs.TryGetValue(ab, out var o) ? (o.Size + sz, o.State, o.Type) : (sz, st, ty);
    }
    foreach (var (b, v) in allocs) if (v.Size >= 0x1000000) Console.WriteLine($"{b:X9} {v.Size / 1048576,6} MB state {v.State:X} type {v.Type:X} {Sym(b)}");
    return;
}
uint exThread = 0;
if (loc.TryGetValue(6, out var ex))
{
    exThread = U32(ex.Rva);
    long rec = ex.Rva + 8;
    uint code = U32(rec); ulong addr = U64(rec + 16); uint np = U32(rec + 24);
    Console.WriteLine($"exception {code:X8} at {addr:X} {Sym(addr)} thread {exThread}");
    for (int i = 0; i < np; i++) { ulong p = U64(rec + 32 + i * 8); Console.WriteLine($"  param{i} {p:X} {Sym(p)}"); }
    if (code == 0xE06D7363 && np >= 3)
    {
        // C++ exception: ThrowInfo (param2) -> CatchableTypeArray -> first CatchableType -> TypeDescriptor name (image-relative, param3 = image base)
        ulong img = np >= 4 ? U64(rec + 32 + 24) : 0, ti = U64(rec + 32 + 16);
        var t = ReadMem(ti, 16);
        if (t != null)
        {
            var cta = ReadMem(img + BitConverter.ToUInt32(t, 12), 8);
            if (cta != null)
            {
                var ct = ReadMem(img + BitConverter.ToUInt32(cta, 4), 8);
                if (ct != null)
                {
                    var td = ReadMem(img + BitConverter.ToUInt32(ct, 4) + 16, 80);
                    if (td != null) Console.WriteLine($"  type {System.Text.Encoding.ASCII.GetString(td).Split('\0')[0]}");
                }
            }
        }
        // what(): std::exception layout = vtable, const char* msg
        var obj = ReadMem(U64(rec + 32 + 8), 16);
        if (obj != null) { var s = ReadMem(BitConverter.ToUInt64(obj, 8), 200); if (s != null) Console.WriteLine($"  what {System.Text.Encoding.ASCII.GetString(s).Split('\0')[0]}"); }
    }
}
else Console.WriteLine("no exception stream");

if (loc.TryGetValue(3, out var tl))
{
    uint n = U32(tl.Rva);
    for (int i = 0; i < n; i++)
    {
        long t = tl.Rva + 4 + i * 48;
        uint id = U32(t);
        if (args.Length < 2 ? id != exThread : args[1] != "all") continue;
        uint ctxRva = U32(t + 44);
        ulong rsp = U64(ctxRva + 0x98), rip = U64(ctxRva + 0xF8);
        Console.WriteLine($"thread {id} rip {rip:X} {Sym(rip)} rsp {rsp:X}");
        var stack = ReadMem(rsp, 0x2000) ?? Array.Empty<byte>();
        int shown = 0;
        for (int o = 0; o + 8 <= stack.Length && shown < 40; o += 8)
        {
            ulong v = BitConverter.ToUInt64(stack, o); string s = Sym(v);
            if (s != "") { Console.WriteLine($"  [{o:X4}] {s}"); shown++; }
        }
    }
}
Console.WriteLine($"modules {mods.Count}: " + string.Join(", ", mods.Select(m => m.Name).Take(80)));
