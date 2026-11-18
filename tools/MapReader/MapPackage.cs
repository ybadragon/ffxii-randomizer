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

    /// <summary>Which separate piece of floor a triangle is on: triangles sharing an edge are one piece unless that
    /// edge is a wall (kind 0x08; a cliff can run between two triangles that touch). The Shred (ozm_c01) is two pieces
    /// this way, so its Greensnake side has no walk to its Henne Mines exit.</summary>
    public int PieceOf(int triangle) => (_pieces ??= Pieces())[triangle];
    int[]? _pieces;
    int[] Pieces()
    {
        var parent = Enumerable.Range(0, Triangles.Length).ToArray();
        int Find(int t) { while (parent[t] != t) t = parent[t] = parent[parent[t]]; return t; }
        var walls = Edges.Where(e => e.Kind == 0x08).Select(e => (Math.Min(e.A, e.B), Math.Max(e.A, e.B))).ToHashSet();
        var byEdge = new Dictionary<(int, int), int>();
        for (int t = 0; t < Triangles.Length; t++)
        {
            var (a, b, c, _) = Triangles[t];
            if (a >= Vertices.Length || b >= Vertices.Length || c >= Vertices.Length) continue;
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                if (walls.Contains((Math.Min(u, v), Math.Max(u, v)))) continue;
                else if (byEdge.TryGetValue((Math.Min(u, v), Math.Max(u, v)), out int o)) parent[Find(o)] = Find(t);
                else byEdge[(Math.Min(u, v), Math.Max(u, v))] = t;
        }
        return Enumerable.Range(0, Triangles.Length).Select(Find).ToArray();
    }

    /// <summary>The triangle holding the point, else the closest one within <paramref name="near"/> units, or -1.</summary>
    public int NearestTriangle(Point2 p, double near)
    {
        int t = TriangleAt(p); if (t >= 0) return t;
        double best = near;
        for (int i = 0; i < Triangles.Length; i++)
        {
            var (a, b, c, _) = Triangles[i];
            if (a >= Vertices.Length || b >= Vertices.Length || c >= Vertices.Length) continue;
            double d = Math.Min(new Segment(Vertices[a], Vertices[b]).DistanceTo(p), Math.Min(new Segment(Vertices[b], Vertices[c]).DistanceTo(p), new Segment(Vertices[c], Vertices[a]).DistanceTo(p)));
            if (d < best) { best = d; t = i; }
        }
        return t;
    }

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
        // whole-map minimaps are the big pictures with the red floor band; packages also hold other big textures (skies)
        _minimaps = Tim2.Find(b).Where(p => p.Width >= 512 && IsMinimap(p)).ToList();
    }

    // A minimap: mostly black or transparent background (over half the picture), with at least 0.3% floor red. Skies and
    // other big textures are bright all over.
    static bool IsMinimap(Tim2.Picture p)
    {
        using var bmp = p.Decode();
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        int dark = 0, floor = 0;
        foreach (int c in px)
        {
            int a = (c >> 24) & 255, r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            if (a < 40 || r + g + b < 60) dark++;
            else if (a > 100 && r > 140 && g < 130 && b < 130) floor++;
        }
        return dark > px.Length / 2 && floor > px.Length * 0.003;
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
