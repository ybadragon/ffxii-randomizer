using System.Drawing;
using System.Drawing.Imaging;
using Ff12Randomizer.Core.GameData;

namespace Ff12MapData;

/// <summary>Floor that exists in one minimap variant and not in another: somewhere the game opens or closes a way.</summary>
public sealed record VariantChange(int FromVariant, int ToVariant, bool Opens, Point2 Min, Point2 Max, int Pixels)
{
    public Point2 Center => new((Min.X + Max.X) / 2, (Min.Z + Max.Z) / 2);
    public double DistanceTo(Point2 p) => Math.Sqrt(Math.Pow(Math.Max(0, Math.Max(Min.X - p.X, p.X - Max.X)), 2) + Math.Pow(Math.Max(0, Math.Max(Min.Z - p.Z, p.Z - Max.Z)), 2));
}

/// <summary>A conditional walk-mesh crossing that isn't one of the map's exits: a gate, door or barrier.</summary>
public sealed record Barrier(Segment Line, int Tag);

/// <summary>
/// Everything known about one map, read from the game's files the first time it's asked for: its script (entrances,
/// exits, what switches its variants), its map package (walk mesh, minimap pictures) and what changes between variants.
/// </summary>
public sealed class MapInfo
{
    public int Id { get; }
    public string Script { get; }
    public string Place { get; }
    public ScriptInfo Info { get; }
    public MapPackage? Package { get; }

    static readonly Dictionary<int, MapInfo?> Cache = new();

    public static MapInfo? For(int mapId)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(mapId, out var m)) return m;
            var name = GameFiles.ScriptName(mapId);
            var info = name is null ? null : ScriptInfo.For(name);
            return Cache[mapId] = info is null ? null : new MapInfo(mapId, name!, info);
        }
    }

    MapInfo(int id, string script, ScriptInfo info)
    {
        Id = id; Script = script; Info = info; Place = GameFiles.PlaceName(id);
        Package = FindPackage();
    }

    /// <summary>
    /// The area's package whose walk mesh holds the most of this map's entrance points (ties: the smaller mesh).
    /// Several packages can cover the same spot (dst_a02's spot is covered by dst0102 and dst0107); only the right one
    /// has all of the map's entrances on its floor.
    /// </summary>
    MapPackage? FindPackage()
    {
        // the answer is remembered in work/cache (derived from the game files, never committed): finding it means reading
        // every package in the area, which is what made each map change take seconds
        var memo = Path.Combine(GameFiles.CacheDir, "derived", "package", Script + ".txt");
        if (File.Exists(memo)) return File.ReadAllText(memo).Trim() is { Length: > 0 } known ? MapPackage.Get(known) : null;
        var found = SearchPackage();
        // written whole then moved into place: another bot process reading it half-written would take it for "no package"
        Directory.CreateDirectory(Path.GetDirectoryName(memo)!);
        var temp = $"{memo}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, found?.Name ?? "");
        try { File.Move(temp, memo, overwrite: true); } catch (IOException) { File.Delete(temp); }
        return found;
    }

    MapPackage? SearchPackage()
    {
        var points = Info.Entrances.Where(e => e.X != 0 || e.Z != 0).Select(e => new Point2(e.X, e.Z)).ToList();
        if (points.Count == 0) points = Info.Exits.Select(x => x.Mid).ToList();
        MapPackage? best = null; int bestHits = 0;
        foreach (var name in MapPackage.InArea(Script[..3]))
        {
            if (MapPackage.Get(name) is not { Mesh: { } mesh } pkg) continue;
            int hits = points.Count(p => mesh.TriangleAt(p) >= 0);
            if (hits > bestHits || hits == bestHits && hits > 0 && mesh.Triangles.Length < best!.Mesh!.Triangles.Length) { best = pkg; bestHits = hits; }
        }
        return best;
    }

    /// <summary>
    /// Whether the walk mesh lets the party walk from <paramref name="from"/> to an exit (to a point 2.5 units inside its
    /// line, toward the map's entrance nearest it). False when the exit's inside is on another piece of floor, or far
    /// off the floor while <paramref name="from"/> is on it (The Shred's exits to Haulo Green and Field of Light Winds:
    /// grass and rock in game). True when the map has no mesh or <paramref name="from"/> isn't near it (no evidence), and
    /// on maps whose script switches the minimap variant (the floor changes there, as Hourglass Basin's quicksand does,
    /// and the mesh alone doesn't say how).
    /// </summary>
    public bool CanWalkTo(Point2 from, Exit x)
    {
        if (Package?.Mesh is not { } m || Info.VariantSwitches.Count > 0) return true;
        int a = m.NearestTriangle(from, 4); if (a < 0) return true;
        var ins = Info.Entrances.Where(e => e.X != 0 || e.Z != 0).OrderBy(e => Math.Pow(e.X - x.Mid.X, 2) + Math.Pow(e.Z - x.Mid.Z, 2)).Select(e => new Point2(e.X, e.Z)).DefaultIfEmpty(from).First();
        double dx = x.Mid.X - ins.X, dz = x.Mid.Z - ins.Z, l = Math.Max(0.01, Math.Sqrt(dx * dx + dz * dz));
        int b = m.NearestTriangle(new(x.Mid.X - dx / l * 2.5, x.Mid.Z - dz / l * 2.5), 4);
        return b >= 0 && m.PieceOf(a) == m.PieceOf(b);
    }

    /// <summary>Conditional crossings of the walk mesh that aren't at one of the map's exits (more than 8 units away).</summary>
    public IReadOnlyList<Barrier> Barriers => _barriers ??= Package?.Mesh is not { } m ? [] :
        m.Edges.Where(e => e.IsConditional && e.IsCrossing && e.A < m.Vertices.Length && e.B < m.Vertices.Length)
            .Select(e => new Barrier(m.Line(e), e.Tag))
            .Where(b => Info.Exits.All(x => Math.Min(x.DistanceTo(b.Line.A), Math.Min(x.DistanceTo(b.Line.B), x.DistanceTo(b.Line.Mid))) > 8))
            .ToList();
    IReadOnlyList<Barrier>? _barriers;

    /// <summary>Where the floor differs between minimap variant 0 and each other variant.</summary>
    public IReadOnlyList<VariantChange> VariantChanges => _changes ??= ComputeChanges();
    IReadOnlyList<VariantChange>? _changes;

    List<VariantChange> ComputeChanges()
    {
        var list = new List<VariantChange>();
        if (Package is not { VariantCount: > 1 } pkg) return list;
        var baseFloor = FloorMask(pkg.Minimap(0));
        for (int v = 1; v < pkg.VariantCount; v++)
        {
            var other = FloorMask(pkg.Minimap(v));
            int w = Math.Min(baseFloor.GetLength(0), other.GetLength(0)), h = Math.Min(baseFloor.GetLength(1), other.GetLength(1));
            var seen = new bool[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (seen[x, y] || baseFloor[x, y] == other[x, y]) continue;
                bool opens = other[x, y];
                var q = new Queue<(int, int)>(); q.Enqueue((x, y)); seen[x, y] = true;
                int n = 0, x0 = x, x1 = x, y0 = y, y1 = y;
                while (q.Count > 0)
                {
                    var (cx, cy) = q.Dequeue(); n++;
                    x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx); y0 = Math.Min(y0, cy); y1 = Math.Max(y1, cy);
                    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[nx, ny] || baseFloor[nx, ny] == other[nx, ny] || other[nx, ny] != opens) continue;
                        seen[nx, ny] = true; q.Enqueue((nx, ny));
                    }
                }
                if (n >= 20) list.Add(new VariantChange(0, v, opens, pkg.ToWorld(x0, y0), pkg.ToWorld(x1, y1), n));
            }
        }
        return list;
    }

    /// <summary>Walkable floor in a minimap picture: its red band.</summary>
    public static bool[,] FloorMask(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        var m = new bool[bmp.Width, bmp.Height];
        for (int i = 0; i < px.Length; i++)
        {
            int a = (px[i] >> 24) & 255, r = (px[i] >> 16) & 255, g = (px[i] >> 8) & 255, b = px[i] & 255;
            m[i % bmp.Width, i / bmp.Width] = a > 100 && r > 140 && g < 130 && b < 130;
        }
        return m;
    }

    /// <summary>
    /// Why the party might be stuck at <paramref name="at"/>, from the map's own data: off the walk mesh, at a barrier
    /// crossing, at floor that only exists in another minimap variant (and what switches it), at a trigger line, or none of
    /// these (likely a battle, a dialog or something the files don't describe).
    /// </summary>
    public IEnumerable<string> Explain(Point2 at, double radius = 6)
    {
        bool any = false;
        if (Package?.Mesh is { } mesh && mesh.TriangleAt(at, 1.0) < 0) { any = true; yield return $"({at.X:F1}, {at.Z:F1}) is off the walk mesh: not walkable floor (a cliff, a plateau or an obstacle)."; }
        foreach (var b in Barriers.Where(b => b.Line.DistanceTo(at) < radius).OrderBy(b => b.Line.DistanceTo(at)))
        {
            any = true;
            yield return $"barrier crossing (condition {b.Tag}) {b.Line.DistanceTo(at):F1} units away, ({b.Line.A.X:F1}, {b.Line.A.Z:F1})-({b.Line.B.X:F1}, {b.Line.B.Z:F1})";
        }
        foreach (var c in VariantChanges.Where(c => c.DistanceTo(at) < radius))
        {
            any = true;
            yield return $"floor that {(c.Opens ? "only exists in" : "is gone in")} minimap variant {c.ToVariant}, ({c.Min.X:F0}, {c.Min.Z:F0})-({c.Max.X:F0}, {c.Max.Z:F0}): {Switches(c.ToVariant)}";
        }
        foreach (var t in Info.OtherTriggers.Where(t => t.Line.DistanceTo(at) < radius))
        {
            any = true;
            yield return $"trigger line {t.TriggerId}{(t.Actor is null ? "" : $" ({t.Actor})")} {t.Line.DistanceTo(at):F1} units away: a scene, message or guard";
        }
        if (!any) yield return "nothing in the map data explains it here: probably a battle, a dialog, an NPC or an obstacle the files don't describe";
    }

    /// <summary>What switches the map to <paramref name="variant"/>: actor, story checks, whether the player has to use it.</summary>
    public string Switches(int variant)
    {
        var who = Info.VariantSwitches.Where(s => s.Variants.Contains(variant)).ToList();
        if (who.Count == 0) return "nothing in the script switches to it";
        return string.Join("; ", who.Select(s => s.Actor
            + (s.StoryChecks.Count > 0 ? $" (story {string.Join(", ", s.StoryChecks.Select(c => $"{c.Compare} {c.Value}").Distinct())})" : "")
            + (s.UsedByPlayer ? ", used by walking up to it and pressing Space" : "")
            + (s.ChecksPreviousMap ? ", depends on the map you came from" : "")));
    }
}
