using System.Drawing;

namespace Ff12MapData;

/// <summary>A walk-mesh edge: <see cref="Tag"/> 0xFF on ordinary edges; anything else marks a conditional crossing
/// (exits, gates, barriers). <see cref="Kind"/> 0x2F joins two cells, 0x08 is a wall.</summary>
public readonly record struct MeshEdge(int A, int B, int Tag, int Kind)
{
    public bool IsCrossing => Kind == 0x2F;
    public bool IsConditional => Tag != 0xFF;
}

/// <summary>The walkable floor as triangles: the mesh the game's AI routes over.</summary>
public sealed class WalkMesh(Point2[] vertices, (int A, int B, int C, int Cell)[] triangles, MeshEdge[] edges)
{
    public Point2[] Vertices { get; } = vertices;
    public (int A, int B, int C, int Cell)[] Triangles { get; } = triangles;
    public MeshEdge[] Edges { get; } = edges;

    public Segment Line(MeshEdge e) => new(Vertices[e.A], Vertices[e.B]);

    /// <summary>The triangle holding the point (with a little tolerance at the edges), or -1.</summary>
    public int TriangleAt(Point2 p, double tolerance = 0.25)
    {
        for (int t = 0; t < Triangles.Length; t++)
        {
            var (a, b, c, _) = Triangles[t];
            if (a >= Vertices.Length || b >= Vertices.Length || c >= Vertices.Length) continue;
            double S(Point2 u, Point2 v) => (v.X - u.X) * (p.Z - u.Z) - (v.Z - u.Z) * (p.X - u.X);
            double s1 = S(Vertices[a], Vertices[b]), s2 = S(Vertices[b], Vertices[c]), s3 = S(Vertices[c], Vertices[a]);
            if (s1 >= -tolerance && s2 >= -tolerance && s3 >= -tolerance || s1 <= tolerance && s2 <= tolerance && s3 <= tolerance) return t;
        }
        return -1;
    }
}

/// <summary>
/// A PS2 map package, ps2data/plan_master/map_ctrl/&lt;area&gt;/&lt;package&gt;/bin/&lt;package&gt;.mpk. It holds:
/// <list type="bullet">
/// <item>an NPS2 record (16-byte aligned tag; floats at +16: world x and z of the minimap's top-left, then the map's width and
/// depth): minimap pixel = (world - origin) x 4</item>
/// <item>the walk mesh: a block starting u32 1, u16 counts (vertices, triangles, edges, cells), u32 0x30, then section offsets
/// from the block: vertices (f32 x, z), triangles (u16 a, b, c, cell), edges (u16 a, b, tag, kind), cell lists, routing</item>
/// <item>the whole-map minimap pictures (TIM2, at least 512 wide; red floor, cyan walls): one per minimap variant
/// (setnavimapindex n shows picture n), so pictures that differ show where the game opens something up</item>
/// </list>
/// </summary>
public sealed class MapPackage
{
    public string Name { get; }
    public Point2 Origin { get; }
    public WalkMesh? Mesh { get; }
    readonly List<Tim2.Picture> _minimaps;
    readonly Dictionary<int, Bitmap> _decoded = new();

    public int VariantCount => _minimaps.Count;

    /// <summary>The minimap picture of variant <paramref name="n"/> (decoded once, then kept).</summary>
    public Bitmap Minimap(int n)
    {
        lock (_decoded)
        {
            if (!_decoded.TryGetValue(n, out var bmp)) _decoded[n] = bmp = _minimaps[Math.Clamp(n, 0, _minimaps.Count - 1)].Decode();
            return bmp;
        }
    }

    public (double U, double V) ToPixel(Point2 w) => ((w.X - Origin.X) * 4, (w.Z - Origin.Z) * 4);
    public Point2 ToWorld(double u, double v) => new(u / 4 + Origin.X, v / 4 + Origin.Z);

    static readonly Dictionary<string, MapPackage?> Loaded = new();

    /// <summary>Package by name ("dst0101"), or null when the game has none by that name.</summary>
    public static MapPackage? Get(string name)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out var p)) return p;
            var data = GameFiles.Get($"ps2data/plan_master/map_ctrl/{name[..5]}/{name}/bin/{name}.mpk");
            return Loaded[name] = data is null ? null : new MapPackage(name, data);
        }
    }

    /// <summary>Names of every package in an area ("dst" gives dst0101, dst0102, ...).</summary>
    public static IEnumerable<string> InArea(string prefix) =>
        GameFiles.List("ps2data/plan_master/map_ctrl/" + prefix).Where(n => n.EndsWith(".mpk")).Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct().Order();

    MapPackage(string name, byte[] b)
    {
        Name = name;
        for (int i = 0; i + 32 <= b.Length; i += 16)
            if (b[i] == 'N' && b[i + 1] == 'P' && b[i + 2] == 'S' && b[i + 3] == '2') { Origin = new(BitConverter.ToSingle(b, i + 16), BitConverter.ToSingle(b, i + 20)); break; }
        Mesh = ReadMesh(b);
        _minimaps = Tim2.Find(b).Where(p => p.Width >= 512).ToList();
    }

    static WalkMesh? ReadMesh(byte[] b)
    {
        for (int o = 0; o + 0x30 < b.Length; o += 16)
        {
            if (BitConverter.ToInt32(b, o) != 1 || BitConverter.ToInt32(b, o + 12) != 0x30) continue;
            int nv = BitConverter.ToUInt16(b, o + 4), nt = BitConverter.ToUInt16(b, o + 6), ne = BitConverter.ToUInt16(b, o + 8);
            if (nv < 3 || nt < 1 || nv > 20000 || nt > 40000 || ne > 40000) continue;
            int vo = o + BitConverter.ToInt32(b, o + 12), to = o + BitConverter.ToInt32(b, o + 16), eo = o + BitConverter.ToInt32(b, o + 20);
            if (vo + nv * 8 > b.Length || to + nt * 8 > b.Length || eo + ne * 8 > b.Length) continue;
            var v = new Point2[nv];
            for (int i = 0; i < nv; i++) v[i] = new(BitConverter.ToSingle(b, vo + i * 8), BitConverter.ToSingle(b, vo + i * 8 + 4));
            var t = new (int, int, int, int)[nt];
            for (int i = 0; i < nt; i++) t[i] = (U16(b, to + i * 8), U16(b, to + i * 8 + 2), U16(b, to + i * 8 + 4), U16(b, to + i * 8 + 6));
            var e = new MeshEdge[ne];
            for (int i = 0; i < ne; i++)
            {
                // third field: 0x00FF on ordinary edges, otherwise the condition number in its high byte
                int raw = U16(b, eo + i * 8 + 4);
                e[i] = new(U16(b, eo + i * 8), U16(b, eo + i * 8 + 2), raw == 0xFF ? 0xFF : raw >> 8, U16(b, eo + i * 8 + 6));
            }
            return new WalkMesh(v, t, e);
        }
        return null;
    }

    static int U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
}
