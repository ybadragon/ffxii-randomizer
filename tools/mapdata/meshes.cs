#:property TargetFramework=net10.0-windows
// meshes <mpk> : every walk mesh block (u32 1, u16 verts/tris/edges/groups, u32 0x30) and its floor-id byte stats
var b = File.ReadAllBytes(args[0]);
for (int i = 0; i + 0x30 < b.Length; i += 16)
{
    if (BitConverter.ToInt32(b, i) != 1 || BitConverter.ToInt32(b, i + 12) != 0x30) continue;
    int nv = BitConverter.ToUInt16(b, i + 4), nt = BitConverter.ToUInt16(b, i + 6), ne = BitConverter.ToUInt16(b, i + 8), ng = BitConverter.ToUInt16(b, i + 10);
    if (nv < 3 || nt < 1 || nv > 20000 || nt > 40000) continue;
    var off = Enumerable.Range(0, 8).Select(k => BitConverter.ToInt32(b, i + 12 + k * 4)).ToArray();
    Console.WriteLine($"{i:X8}: {nv} verts {nt} tris {ne} edges {ng} groups, sections {string.Join(",", off.Select(o => o.ToString("X")))}");
}
