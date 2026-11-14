using System.Drawing;
using System.Runtime.InteropServices;

// Reads the in-game minimap (top right, north up, centred on the party leader) from the screen and turns it into a
// walkable grid. Pixel layout measured on the 1600x900 window: minimap crop at window (1260, 30), 280x280.
static class Minimap
{
    public const int Left = 1260, Top = 30, Size = 280;

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    public static Bitmap Capture(IntPtr window)
    {
        GetWindowRect(window, out var r);
        var bmp = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.L + Left, r.T + Top, 0, 0, new System.Drawing.Size(Size, Size));
        return bmp;
    }

    public enum Cell : byte { Outside, Floor, Edge, Marker }

    // Floor is the blue fill; edges are the bright outlines; the camera's green view cone sits on top of both, so a
    // pixel only counts as floor when blue is strong and at least as strong as green.
    public static Cell Classify(Color c)
    {
        // sampled in Golmore: outside (9,37,44), floor (11,44,80)..(71,113,115), wall glow bright with blue >= green,
        // cone over floor (81,177,140), cone over outside (42,106,109), cone's own edge (171,255,206)
        if (c.R > 200 && c.G > 70 && c.G < 170 && c.B < 90) return Cell.Marker;
        // wall lines (127..136, 174..183, 164..169): green a little above blue; the cone's own edge (171,255,206) has
        // green far above blue and is not a wall
        if (c.R > 90 && c.G > 140 && c.G - c.B < 30) return Cell.Edge;
        if (c.B > 55 && c.B - c.G > 18) return Cell.Floor;
        if (c.G - c.B > 15 && c.G > 100 && c.R < 150) return Cell.Floor; // under the view cone
        return Cell.Outside;
    }

    public static Cell[,] Grid(Bitmap bmp, double? coneAngle = null)
    {
        var grid = new Cell[Size, Size];
        var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[Size * Size];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                grid[x, y] = Classify(Color.FromArgb(px[y * Size + x]));
        // Exit bars (teal) look like the camera cone over floor. The cone is a wedge from the marker along the camera
        // direction (coneAngle, minimap pixel angle); teal anywhere else is an exit and must not be walked into.
        if (coneAngle is double ca && Marker(grid) is { } mc)
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var c = Color.FromArgb(px[y * Size + x]);
                    if (grid[x, y] == Cell.Marker) continue;
                    double dx = x - mc.x, dy = y - mc.y, dist = Math.Sqrt(dx * dx + dy * dy);
                    double off = Math.Abs(Math.IEEERemainder(Math.Atan2(dy, dx) - ca, 2 * Math.PI));
                    bool inCone = dist < 12 || (dist < 100 && off < 0.9);
                    if (inCone)
                    {
                        // seen through the cone (sampled in Golmore): wall glow (113..135, 216..228, 178..193),
                        // floor (50..60, 120..143, 113..132), outside (32..56, 85..125, 69..90)
                        // the cone's own outline (171,255,206) is just an overlay line, not a wall
                        grid[x, y] = c.R > 150 && c.G > 240 ? Cell.Floor : c.G > 200 && c.B > 170 ? Cell.Edge : c.B >= 100 || c.G > 150 ? Cell.Floor : Cell.Outside;
                    }
                    else if (c.G > 130 && c.R < 110 && c.G - c.B > 15) grid[x, y] = Cell.Edge; // teal exit bar
                }
        // Minimap colours differ per region (blue jungle, beige desert...). Learn this map's floor colour from a ring
        // around the party marker (skipping the marker, the cone and bright outlines) and accept close colours too.
        int floorCount = 0; foreach (var cell in grid) if (cell == Cell.Floor) floorCount++;
        var m = floorCount < 2000 ? Marker(grid) : null; // blue-style maps already classify well
        if (m is { } mk)
        {
            var samples = new List<Color>();
            for (int r = 12; r <= 20; r += 2)
                for (int a = 0; a < 360; a += 10)
                {
                    int x = (int)(mk.x + r * Math.Cos(a * Math.PI / 180)), y = (int)(mk.y + r * Math.Sin(a * Math.PI / 180));
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    var c = Color.FromArgb(px[y * Size + x]);
                    if (grid[x, y] == Cell.Marker || grid[x, y] == Cell.Edge || c.G - Math.Max(c.R, c.B) > 25) continue;
                    samples.Add(c);
                }
            if (samples.Count > 10)
            {
                int Med(Func<Color, int> f) { var v = samples.Select(f).OrderBy(t => t).ToList(); return v[v.Count / 2]; }
                int fr = Med(c => c.R), fg = Med(c => c.G), fb = Med(c => c.B), lum = fr + fg + fb;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        if (grid[x, y] != Cell.Outside) continue;
                        var c = Color.FromArgb(px[y * Size + x]);
                        int d = Math.Abs(c.R - fr) + Math.Abs(c.G - fg) + Math.Abs(c.B - fb);
                        if (c.R + c.G + c.B > 640 && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 45) grid[x, y] = Cell.Edge; // near-white outline
                        else if (d < 90) grid[x, y] = Cell.Floor;
                    }
            }
        }
        // Fill holes: dark floor patches inside the map read as Outside. Any Outside area that cannot reach the minimap
        // border without crossing floor or walls is enclosed, so it is floor (enclosed wall bodies are unreachable anyway).
        var seen = new bool[Size, Size]; var fq = new Queue<(int, int)>();
        // walls thickened by 2 px for this flood only, so small gaps in an outline do not let the outside in
        var wall = new bool[Size, Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (grid[x, y] == Cell.Edge)
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < Size && ay < Size) wall[ax, ay] = true; }
        for (int i = 0; i < Size; i++)
            foreach (var (bx, by) in new[] { (i, 0), (i, Size - 1), (0, i), (Size - 1, i) })
                if (grid[bx, by] == Cell.Outside && !wall[bx, by] && !seen[bx, by]) { seen[bx, by] = true; fq.Enqueue((bx, by)); }
        while (fq.Count > 0)
        {
            var (qx, qy) = fq.Dequeue();
            foreach (var (nx, ny) in new[] { (qx + 1, qy), (qx - 1, qy), (qx, qy + 1), (qx, qy - 1) })
                if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && !seen[nx, ny] && !wall[nx, ny] && grid[nx, ny] == Cell.Outside) { seen[nx, ny] = true; fq.Enqueue((nx, ny)); }
        }
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) if (grid[x, y] == Cell.Outside && !seen[x, y]) grid[x, y] = Cell.Floor;
        return grid;
    }

    // Centre of the orange party marker ring.
    public static (double x, double y)? Marker(Cell[,] grid)
    {
        double sx = 0, sy = 0; int n = 0;
        // the party marker sits at the minimap centre; other orange icons (crystals, exits) are elsewhere
        for (int y = 113; y < 163; y++) for (int x = 110; x < 160; x++) if (grid[x, y] == Cell.Marker) { sx += x; sy += y; n++; }
        return n < 5 ? null : (sx / n, sy / n);
    }

    // Style-free reading (no per-region colours): walls are thin lines brighter than their surroundings in every
    // minimap style seen so far (blue glow, white outline, grey). Floor is whatever the party can reach from its marker
    // without crossing a wall.
    public const double ConeHalfAngle = 0.74;
    public static string LastInfo = "";
    public static double MergeBelow = double.TryParse(Environment.GetEnvironmentVariable("MMMERGE"), out var mb) ? mb : 60; // patch borders weaker than this are not walls // measured: the view cone spans about 42 degrees either side

    public static Cell[,] EdgeGrid(Bitmap bmp, double? coneAngle = null, int ridge = 18)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[Size * Size];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        var lum = new float[Size, Size];
        var grid = new Cell[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var c = Color.FromArgb(px[y * Size + x]);
                lum[x, y] = 0.30f * c.R + 0.59f * c.G + 0.11f * c.B;
                if (Classify(c) == Cell.Marker) grid[x, y] = Cell.Marker;
            }
        // local mean over a 9x9 box (integral image)
        var sum = new double[Size + 1, Size + 1];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            sum[x + 1, y + 1] = lum[x, y] + sum[x, y + 1] + sum[x + 1, y] - sum[x, y];
        double Mean(int x, int y, int r)
        {
            int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r), x1 = Math.Min(Size, x + r + 1), y1 = Math.Min(Size, y + r + 1);
            return (sum[x1, y1] - sum[x0, y1] - sum[x1, y0] + sum[x0, y0]) / ((x1 - x0) * (y1 - y0));
        }
        var m = Marker(grid) ?? (Size / 2.0, Size / 2.0);
        var wall = new bool[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (grid[x, y] == Cell.Marker) continue;
                if (lum[x, y] - Mean(x, y, 4) < ridge) continue;
                if ((x - m.x) * (x - m.x) + (y - m.y) * (y - m.y) < 144) continue; // the marker icon's own bright rim
                if (coneAngle is double ca)
                {
                    // the cone's own two edge lines are bright too: skip ridges lying along them
                    double dx = x - m.x, dy = y - m.y, dist = Math.Sqrt(dx * dx + dy * dy);
                    double off = Math.Abs(Math.IEEERemainder(Math.Atan2(dy, dx) - ca, 2 * Math.PI));
                    if (dist < 110 && Math.Abs(off - ConeHalfAngle) < 0.09) continue;
                }
                wall[x, y] = true;
            }
        // Walls are long lines; footprint dots, NPC dots and icons make small isolated bright blobs. Drop pieces that
        // are small or short.
        var label = new bool[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (!wall[x, y] || label[x, y]) continue;
                var piece = new List<(int, int)>(); var st = new Stack<(int, int)>(); st.Push((x, y)); label[x, y] = true;
                int minx = x, maxx = x, miny = y, maxy = y;
                while (st.Count > 0)
                {
                    var (cx, cy) = st.Pop(); piece.Add((cx, cy));
                    minx = Math.Min(minx, cx); maxx = Math.Max(maxx, cx); miny = Math.Min(miny, cy); maxy = Math.Max(maxy, cy);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && wall[nx, ny] && !label[nx, ny]) { label[nx, ny] = true; st.Push((nx, ny)); }
                    }
                }
                if (piece.Count < 30 || Math.Max(maxx - minx, maxy - miny) < 8)
                    foreach (var (px2, py2) in piece) wall[px2, py2] = false;
            }
        // the minimap panel fades out at its border; treat the outer band as wall so inside and outside don't join there
        const int band = 22;
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (x < band || y < band || x >= Size - band || y >= Size - band) wall[x, y] = true;
        // close small gaps in outlines
        var thick = new bool[Size, Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (wall[x, y])
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < Size && ay < Size) thick[ax, ay] = true; }
        // floor = reachable from the marker without crossing a wall. Flood with walls thickened by `seal` px so gaps up
        // to ~2*seal px in an outline don't let the flood escape, then grow the floor back out to the thin walls.
        const int seal = 2;
        var barrier = new bool[Size, Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (wall[x, y])
                for (int dy = -seal; dy <= seal; dy++) for (int dx = -seal; dx <= seal; dx++)
                { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < Size && ay < Size && dx * dx + dy * dy <= seal * seal) barrier[ax, ay] = true; }
        var q = new Queue<(int, int)>();
        int sx = (int)Math.Round(m.x), sy = (int)Math.Round(m.y);
        for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++)
        {
            int x = sx + dx, y = sy + dy;
            if (x >= 0 && y >= 0 && x < Size && y < Size && dx * dx + dy * dy <= 36 && !thick[x, y] && grid[x, y] == Cell.Outside)
            { grid[x, y] = Cell.Floor; q.Enqueue((x, y)); }
        }
        // Colour check: learn the floor colour around the marker (outside the cone) and, separately, as seen through the
        // cone; the flood only spreads over pixels close to the matching one. Icons and the cone can hide stretches of
        // outline, and this stops the flood leaking through those gaps into the surroundings.
        var ringOut = new List<Color>(); var ringCone = new List<Color>();
        bool InCone(int x, int y)
        {
            if (coneAngle is not double cang) return false;
            double ddx = x - m.x, ddy = y - m.y, d = Math.Sqrt(ddx * ddx + ddy * ddy);
            return d < 100 && Math.Abs(Math.IEEERemainder(Math.Atan2(ddy, ddx) - cang, 2 * Math.PI)) < ConeHalfAngle;
        }
        for (int r = 8; r <= 18; r += 2)
            for (int a = 0; a < 360; a += 6)
            {
                int x = (int)(m.x + r * Math.Cos(a * Math.PI / 180)), y = (int)(m.y + r * Math.Sin(a * Math.PI / 180));
                if (x < 0 || y < 0 || x >= Size || y >= Size || wall[x, y] || grid[x, y] == Cell.Marker) continue;
                { var rc = Color.FromArgb(px[y * Size + x]); if (rc.R > rc.G + 40) continue; } // the marker's red direction arrow
                (InCone(x, y) ? ringCone : ringOut).Add(Color.FromArgb(px[y * Size + x]));
            }
        (int r, int g, int b, int tol)? Seed(List<Color> s)
        {
            if (s.Count < 8) return null;
            int Med(Func<Color, int> f) { var v = s.Select(f).OrderBy(t => t).ToList(); return v[v.Count / 2]; }
            int mr = Med(c => c.R), mg = Med(c => c.G), mb = Med(c => c.B);
            var dev = s.Select(c => Math.Abs(c.R - mr) + Math.Abs(c.G - mg) + Math.Abs(c.B - mb)).OrderBy(t => t).ToList();
            return (mr, mg, mb, Math.Clamp(dev[dev.Count / 2] * 4, 40, 90));
        }
        var seedOut = Seed(ringOut); var seedCone = Seed(ringCone) ?? seedOut;
        // outside reference: the band just inside the panel edge is mostly surroundings
        var bandPx = new List<Color>();
        for (int y = 24; y < Size - 24; y += 2) for (int x = 24; x < Size - 24; x += 2)
            if ((x < 34 || y < 34 || x >= Size - 34 || y >= Size - 34) && !wall[x, y] && !InCone(x, y)) bandPx.Add(Color.FromArgb(px[y * Size + x]));
        var seedBg = Seed(bandPx);
        // The party can stand right on a corridor's edge, so the ring around the marker mixes floor and surroundings.
        // Split the ring into two colour groups and take as floor the group least like the outside reference.
        (int r, int g, int b, int tol)? FloorSeed(List<Color> s)
        {
            if (s.Count < 8) return null;
            if (seedBg is not { } bgc) return Seed(s);
            var a = s.OrderBy(c => Math.Abs(c.R - bgc.r) + Math.Abs(c.G - bgc.g) + Math.Abs(c.B - bgc.b)).ToList();
            var c1 = a[a.Count / 4]; var c2 = a[a.Count * 3 / 4];
            List<Color> g1 = new(), g2 = new();
            for (int it = 0; it < 6; it++)
            {
                g1.Clear(); g2.Clear();
                foreach (var c in s)
                    ((Math.Abs(c.R - c1.R) + Math.Abs(c.G - c1.G) + Math.Abs(c.B - c1.B)) <= (Math.Abs(c.R - c2.R) + Math.Abs(c.G - c2.G) + Math.Abs(c.B - c2.B)) ? g1 : g2).Add(c);
                if (g1.Count > 0) c1 = Color.FromArgb((int)g1.Average(c => c.R), (int)g1.Average(c => c.G), (int)g1.Average(c => c.B));
                if (g2.Count > 0) c2 = Color.FromArgb((int)g2.Average(c => c.R), (int)g2.Average(c => c.G), (int)g2.Average(c => c.B));
            }
            int D(Color c) => Math.Abs(c.R - bgc.r) + Math.Abs(c.G - bgc.g) + Math.Abs(c.B - bgc.b);
            var pick = (g2.Count >= 4 && (g1.Count < 4 || D(c2) > D(c1))) ? g2 : g1;
            return Seed(pick.Count >= 8 ? pick : s);
        }
        seedOut = FloorSeed(ringOut); seedCone = FloorSeed(ringCone) ?? seedOut;
        LastInfo = $"floor {seedOut} cone {seedCone} outside {seedBg} ring {ringOut.Count}/{ringCone.Count}";
        bool Like(int x, int y)
        {
            var sd = InCone(x, y) ? seedCone : seedOut;
            if (sd is not { } s) return true;
            var c = Color.FromArgb(px[y * Size + x]);
            int dFloor = Math.Abs(c.R - s.r) + Math.Abs(c.G - s.g) + Math.Abs(c.B - s.b);
            if (dFloor >= s.tol) return false;
            if (seedBg is { } bg && !InCone(x, y)) { int dBg = Math.Abs(c.R - bg.r) + Math.Abs(c.G - bg.g) + Math.Abs(c.B - bg.b); if (dBg < dFloor) return false; }
            return true;
        }
        void Flood(bool[,] block)
        {
            // the marker icon is passable (its ring encloses the start), but stays drawn as the marker
            var through = new bool[Size, Size];
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                    bool near = (nx - m.x) * (nx - m.x) + (ny - m.y) * (ny - m.y) < 196;
                    if (near ? thick[nx, ny] : block[nx, ny]) continue;
                    if (grid[nx, ny] == Cell.Marker) { if (!through[nx, ny]) { through[nx, ny] = true; q.Enqueue((nx, ny)); } continue; }
                    if (grid[nx, ny] == Cell.Outside && (near || Like(nx, ny))) { grid[nx, ny] = Cell.Floor; q.Enqueue((nx, ny)); }
                }
            }
        }
        int startCells = q.Count;
        Flood(barrier);
        // Icons (crystals, NPC dots) and the cone can cover a corridor. Bridge such blockages: close the floor mask
        // (grow 8 px over non-walls, then shrink 8 px), add what that fills in, and keep flooding from there.
        for (int pass = 0; pass < 3; pass++)
        {
            const int cr = 8;
            var floorNow = new bool[Size, Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) floorNow[x, y] = grid[x, y] == Cell.Floor || grid[x, y] == Cell.Marker;
            var dil = new bool[Size, Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                if (floorNow[x, y])
                    for (int dy = -cr; dy <= cr; dy++) for (int dx = -cr; dx <= cr; dx++)
                    { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < Size && ay < Size && dx * dx + dy * dy <= cr * cr && !thick[ax, ay]) dil[ax, ay] = true; }
            int added = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (!dil[x, y] || floorNow[x, y] || grid[x, y] != Cell.Outside) continue;
                    // erosion: keep only pixels whose whole disc is inside the grown mask (or walls), i.e. real gaps
                    bool keep = true;
                    for (int dy = -cr; dy <= cr && keep; dy += 2) for (int dx = -cr; dx <= cr && keep; dx += 2)
                    {
                        int ax = x + dx, ay = y + dy;
                        if (dx * dx + dy * dy > cr * cr) continue;
                        if (ax < 0 || ay < 0 || ax >= Size || ay >= Size || !(dil[ax, ay] || thick[ax, ay])) keep = false;
                    }
                    if (keep) { grid[x, y] = Cell.Floor; q.Enqueue((x, y)); added++; }
                }
            if (added == 0) break;
            Flood(barrier);
        }
        { int fl = 0, lk = 0, near = 0; for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) { if (grid[x, y] == Cell.Floor) fl++; double dd = (x - m.x) * (x - m.x) + (y - m.y) * (y - m.y); if (dd < 900 && !thick[x, y]) { near++; if (Like(x, y)) lk++; } } LastInfo += $" start {startCells} flooded {fl} like {lk}/{near} within 30px"; }
        // grow back: `seal` rounds of one-pixel growth over anything that isn't a thin wall
        for (int round = 0; round < 4; round++)
        {
            var grow = new List<(int, int)>();
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                if (grid[x, y] == Cell.Outside && !thick[x, y])
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                        if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && grid[nx, ny] == Cell.Floor) { grow.Add((x, y)); break; }
            foreach (var (x, y) in grow) grid[x, y] = Cell.Floor;
        }
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (thick[x, y] && grid[x, y] != Cell.Marker) grid[x, y] = Cell.Edge;
        return grid;
    }

    // Marker-controlled watershed. Boundary strength = colour gradient (Sobel on a smoothed image, strongest channel).
    // Two seeds: the party marker (always on floor) and the panel's edge band (surroundings). Both floods grow in
    // order of boundary strength and meet at the strongest lines between them, so a gap in an outline only causes a
    // local error instead of a leak. No per-region colours.
    public static Cell[,] WatershedGrid(Bitmap bmp, double? coneAngle = null)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[Size * Size];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        var grid = new Cell[Size, Size];
        var ch = new float[3][,];
        for (int k = 0; k < 3; k++) ch[k] = new float[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var c = Color.FromArgb(px[y * Size + x]);
                ch[0][x, y] = c.R; ch[1][x, y] = c.G; ch[2][x, y] = c.B;
                if (Classify(c) == Cell.Marker) grid[x, y] = Cell.Marker;
            }
        // smooth: two 3x3 box passes (close to a small Gaussian), keeps texture noise out of the gradient
        float[,] Blur(float[,] a)
        {
            var o = new float[Size, Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float s = 0; int n = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < Size && ay < Size) { s += a[ax, ay]; n++; } }
                    o[x, y] = s / n;
                }
            return o;
        }
        for (int k = 0; k < 3; k++) ch[k] = Blur(Blur(ch[k]));
        var m = Marker(grid) ?? (Size / 2.0, Size / 2.0);
        // Sobel gradient magnitude, strongest of the three channels
        var grad = new int[Size, Size];
        for (int y = 1; y < Size - 1; y++)
            for (int x = 1; x < Size - 1; x++)
            {
                double best = 0;
                for (int k = 0; k < 3; k++)
                {
                    var a = ch[k];
                    double gx = (a[x + 1, y - 1] + 2 * a[x + 1, y] + a[x + 1, y + 1]) - (a[x - 1, y - 1] + 2 * a[x - 1, y] + a[x - 1, y + 1]);
                    double gy = (a[x - 1, y + 1] + 2 * a[x, y + 1] + a[x + 1, y + 1]) - (a[x - 1, y - 1] + 2 * a[x, y - 1] + a[x + 1, y - 1]);
                    best = Math.Max(best, Math.Sqrt(gx * gx + gy * gy));
                }
                grad[x, y] = (int)Math.Min(1000, best);
                if (coneAngle is double ca)
                {
                    // the cone's two edge lines are not walls
                    double dx = x - m.x, dy = y - m.y, dist = Math.Sqrt(dx * dx + dy * dy);
                    double off = Math.Abs(Math.IEEERemainder(Math.Atan2(dy, dx) - ca, 2 * Math.PI));
                    if (dist < 110 && Math.Abs(off - ConeHalfAngle) < 0.1) grad[x, y] = 0;
                }
                if ((x - m.x) * (x - m.x) + (y - m.y) * (y - m.y) < 144) grad[x, y] = 0; // the marker icon itself
            }
        // 1) over-segmentation: one seed per 8x8 block at its calmest pixel, plus the marker disc as its own seed
        var label = new int[Size, Size]; // 0 = unassigned
        var pq = new PriorityQueue<(int x, int y), long>();
        long order = 0;
        int next = 1;
        void Seed(int x, int y, int l) { if (label[x, y] != 0) return; label[x, y] = l; pq.Enqueue((x, y), (long)grad[x, y] * 1_000_000 + order++); }
        int markerLabel = next++;
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if ((x - m.x) * (x - m.x) + (y - m.y) * (y - m.y) <= 36) Seed(x, y, markerLabel);
        const int cell = 8;
        for (int by = 0; by < Size; by += cell)
            for (int bx = 0; bx < Size; bx += cell)
            {
                int bestx = -1, besty = -1, bestg = int.MaxValue;
                for (int y = by; y < Math.Min(Size, by + cell); y++) for (int x = bx; x < Math.Min(Size, bx + cell); x++)
                    if (label[x, y] == 0 && grad[x, y] < bestg) { bestg = grad[x, y]; bestx = x; besty = y; }
                if (bestx >= 0) Seed(bestx, besty, next++);
            }
        // 2) watershed: grow all seeds in order of boundary strength; patches meet along the image's edges
        while (pq.TryDequeue(out var c, out _))
            foreach (var (nx, ny) in new[] { (c.x + 1, c.y), (c.x - 1, c.y), (c.x, c.y + 1), (c.x, c.y - 1) })
                if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && label[nx, ny] == 0)
                { label[nx, ny] = label[c.x, c.y]; pq.Enqueue((nx, ny), (long)grad[nx, ny] * 1_000_000 + order++); }
        // 3) border strength between neighbouring patches = mean gradient along their shared border
        var edgeSum = new Dictionary<long, (double sum, int n)>();
        for (int y = 0; y < Size - 1; y++)
            for (int x = 0; x < Size - 1; x++)
                foreach (var (nx, ny) in new[] { (x + 1, y), (x, y + 1) })
                {
                    int a = label[x, y], b = label[nx, ny];
                    if (a == b) continue;
                    long key = a < b ? (long)a * 100000 + b : (long)b * 100000 + a;
                    double gval = Math.Max(grad[x, y], grad[nx, ny]);
                    edgeSum[key] = edgeSum.TryGetValue(key, out var e) ? (e.sum + gval, e.n + 1) : (gval, 1);
                }
        // 4) patch colours (smoothed image, marker icon excluded)
        var sumC = new double[next, 3]; var cnt = new int[next];
        bool[] touchesBand = new bool[next];
        const int band = 24;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int l = label[x, y];
                if (x < band || y < band || x >= Size - band || y >= Size - band) touchesBand[l] = true;
                if (grid[x, y] == Cell.Marker || (x - m.x) * (x - m.x) + (y - m.y) * (y - m.y) < 144) continue;
                sumC[l, 0] += ch[0][x, y]; sumC[l, 1] += ch[1][x, y]; sumC[l, 2] += ch[2][x, y]; cnt[l]++;
            }
        var nbr = new Dictionary<int, List<(int other, double strength)>>();
        foreach (var (key, e) in edgeSum)
        {
            int a = (int)(key / 100000), b = (int)(key % 100000); double s = e.sum / e.n;
            (nbr.TryGetValue(a, out var la) ? la : nbr[a] = new()).Add((b, s));
            (nbr.TryGetValue(b, out var lb) ? lb : nbr[b] = new()).Add((a, s));
        }
        // floor reference: patches joined to the marker's patch across clearly weak borders
        var core = new HashSet<int> { markerLabel }; var stack = new Stack<int>(); stack.Push(markerLabel);
        while (stack.Count > 0)
            foreach (var (o, s) in nbr.GetValueOrDefault(stack.Pop()) ?? new())
                if (s < MergeBelow * 0.6 && core.Add(o)) stack.Push(o);
        (double r, double g, double b) Mean(IEnumerable<int> ls)
        {
            double r = 0, g = 0, b = 0; int n = 0;
            foreach (var l in ls) { r += sumC[l, 0]; g += sumC[l, 1]; b += sumC[l, 2]; n += cnt[l]; }
            return n == 0 ? (0, 0, 0) : (r / n, g / n, b / n);
        }
        var floorRef = Mean(core);
        var outRef = Mean(Enumerable.Range(1, next - 1).Where(l => touchesBand[l] && !core.Contains(l)));
        double Dist((double r, double g, double b) a, int l) => cnt[l] == 0 ? 0 : Math.Abs(sumC[l, 0] / cnt[l] - a.r) + Math.Abs(sumC[l, 1] / cnt[l] - a.g) + Math.Abs(sumC[l, 2] / cnt[l] - a.b);
        bool FloorLike(int l) => cnt[l] == 0 || Dist(floorRef, l) < Dist(outRef, l);
        // 5) grow the floor over floor-coloured patches, never across a strong (wall) border
        var floorSet = new HashSet<int>(core); foreach (var l in core) stack.Push(l);
        while (stack.Count > 0)
            foreach (var (o, s) in nbr.GetValueOrDefault(stack.Pop()) ?? new())
                if (!floorSet.Contains(o) && s < MergeBelow * 2 && FloorLike(o) && floorSet.Add(o)) stack.Push(o);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (grid[x, y] != Cell.Marker) grid[x, y] = floorSet.Contains(label[x, y]) ? Cell.Floor : Cell.Outside;
        LastInfo = $"watershed: {next - 1} patches, core {core.Count}, floor {floorSet.Count}, floorRef ({floorRef.r:F0},{floorRef.g:F0},{floorRef.b:F0}) outRef ({outRef.r:F0},{outRef.g:F0},{outRef.b:F0})";
        return grid;
    }

    // Edge strength map of any image, normalised to 0..1: brightness (with alpha, for the map pictures) smoothed, Sobel
    // magnitude, smoothed again so near-misses still score.
    public static float[,] EdgeMap(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[w * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        var l = new float[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = Color.FromArgb(px[y * w + x]);
                l[x, y] = (0.30f * c.R + 0.59f * c.G + 0.11f * c.B) * c.A / 255f;
            }
        float[,] Blur(float[,] a)
        {
            var o = new float[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; int n = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    { int ax = x + dx, ay = y + dy; if (ax >= 0 && ay >= 0 && ax < w && ay < h) { s += a[ax, ay]; n++; } }
                    o[x, y] = s / n;
                }
            return o;
        }
        l = Blur(Blur(l));
        var g = new float[w, h]; float max = 1;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float gx = (l[x + 1, y - 1] + 2 * l[x + 1, y] + l[x + 1, y + 1]) - (l[x - 1, y - 1] + 2 * l[x - 1, y] + l[x - 1, y + 1]);
                float gy = (l[x - 1, y + 1] + 2 * l[x, y + 1] + l[x + 1, y + 1]) - (l[x - 1, y - 1] + 2 * l[x, y - 1] + l[x + 1, y - 1]);
                g[x, y] = MathF.Sqrt(gx * gx + gy * gy); max = Math.Max(max, g[x, y]);
            }
        g = Blur(g);
        max = 1; foreach (var v in g) max = Math.Max(max, v);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) g[x, y] /= max;
        return g;
    }

    // Floor mask of a clean map picture: 1 where the pixel differs from the picture's background (its corner).
    public static float[,] FloorMask(Bitmap pic)
    {
        int w = pic.Width, h = pic.Height;
        var data = pic.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[w * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        pic.UnlockBits(data);
        var bg = Color.FromArgb(px[2 * w + 2]);
        var f = new float[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = Color.FromArgb(px[y * w + x]);
                f[x, y] = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) + Math.Abs(c.A - bg.A) > 40 ? 1 : 0;
            }
        return f;
    }

    static bool Open(Cell c) => c == Cell.Floor || c == Cell.Marker;

    // Pixel shift (dx, dy) that best lines up grid b with grid a (b[x,y] ~ a[x+dx,y+dy]), floor cells only.
    public static (int dx, int dy) Shift(Cell[,] a, Cell[,] b, int range = 60)
    {
        int best = -1; (int, int) arg = (0, 0);
        for (int dy = -range; dy <= range; dy++)
            for (int dx = -range; dx <= range; dx++)
            {
                int score = 0;
                for (int y = 20; y < Size - 20; y += 2)
                {
                    int ay = y + dy; if (ay < 0 || ay >= Size) continue;
                    for (int x = 20; x < Size - 20; x += 2)
                    {
                        int ax = x + dx; if (ax < 0 || ax >= Size) continue;
                        if (b[x, y] == Cell.Floor && a[ax, ay] == Cell.Floor) score++;
                    }
                }
                if (score > best) { best = score; arg = (dx, dy); }
            }
        return arg;
    }

    // Shortest path over open cells from start to the open cell closest to goal (by straight distance), preferring
    // cells away from walls. Returns the pixel path, start first.
    public static List<(int x, int y)> Path(Cell[,] grid, (int x, int y) start, (double x, double y) goal, int minClear = 3)
    {
        // distance to the nearest closed cell, capped, so the route keeps to the middle of corridors
        var clear = new int[Size, Size];
        var q = new Queue<(int, int)>();
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (!Open(grid[x, y])) { clear[x, y] = 0; q.Enqueue((x, y)); } else clear[x, y] = int.MaxValue;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && clear[nx, ny] > clear[x, y] + 1) { clear[nx, ny] = clear[x, y] + 1; q.Enqueue((nx, ny)); }
        }
        // the marker and the view cone can leave holes right around the party: open a small disc at the start
        for (int dy = -12; dy <= 12; dy++) for (int dx = -12; dx <= 12; dx++)
        {
            int x = start.x + dx, y = start.y + dy;
            if (x >= 0 && y >= 0 && x < Size && y < Size && dx * dx + dy * dy <= 144 && clear[x, y] == 0 && grid[x, y] != Cell.Edge) clear[x, y] = 2;
        }
        var dist = new double[Size, Size]; var prev = new (int, int)[Size, Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) dist[x, y] = double.MaxValue;
        var pq = new PriorityQueue<(int x, int y), double>();
        dist[start.x, start.y] = 0; pq.Enqueue(start, 0);
        (int x, int y) bestCell = start; double bestGoal = double.MaxValue;
        while (pq.TryDequeue(out var c, out double d))
        {
            if (d > dist[c.x, c.y]) continue;
            double g = Math.Sqrt((c.x - goal.x) * (c.x - goal.x) + (c.y - goal.y) * (c.y - goal.y));
            if (g < bestGoal) { bestGoal = g; bestCell = c; }
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = c.x + dx, ny = c.y + dy;
                if (nx < 0 || ny < 0 || nx >= Size || ny >= Size || clear[nx, ny] == 0 || (clear[nx, ny] < minClear && (nx - start.x) * (nx - start.x) + (ny - start.y) * (ny - start.y) > 144)) continue; // keep 3 px off walls
                double step = (dx != 0 && dy != 0 ? 1.414 : 1) * (1 + 20.0 / Math.Min(clear[nx, ny], 14));
                if (d + step < dist[nx, ny]) { dist[nx, ny] = d + step; prev[nx, ny] = c; pq.Enqueue((nx, ny), d + step); }
            }
        }
        var path = new List<(int x, int y)>();
        for (var c = bestCell; c != start; c = prev[c.x, c.y]) path.Add(c);
        path.Add(start); path.Reverse();
        return path;
    }

    // Furthest index along the path that is visible from path[0] in a straight line over open cells (keeping a
    // one-pixel margin), so a straight walk to it doesn't cut a corner.
    public static int Visible(Cell[,] grid, List<(int x, int y)> path, int max = 60)
    {
        bool Clear(int x, int y)
        {
            for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
            {
                int ax = x + dx, ay = y + dy;
                if (ax < 0 || ay < 0 || ax >= Size || ay >= Size || grid[ax, ay] == Cell.Edge || grid[ax, ay] == Cell.Outside) return false;
            }
            return true;
        }
        var s = path[0]; int best = Math.Min(3, path.Count - 1);
        for (int i = 4; i < path.Count && i <= max; i++)
        {
            var t = path[i]; int n = Math.Max(Math.Abs(t.x - s.x), Math.Abs(t.y - s.y)); bool ok = true;
            for (int j = 1; j <= n && ok; j++)
            {
                int x = s.x + (t.x - s.x) * j / n, y = s.y + (t.y - s.y) * j / n;
                // the disc around the party is often hidden by the marker and cone; only check beyond it
                if ((x - s.x) * (x - s.x) + (y - s.y) * (y - s.y) > 196 && !Clear(x, y)) ok = false;
            }
            if (ok) best = i;
        }
        return best;
    }

    // Debug picture: the captured minimap (left) and the classification with route and leg point (right), 2x scale.
    public static void SaveDebug(string file, Bitmap shot, Cell[,] grid, List<(int x, int y)> path, (int x, int y) leg, (double x, double y) goal)
    {
        using var img = new Bitmap(Size * 4, Size * 2);
        using var g = Graphics.FromImage(img);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.DrawImage(shot, 0, 0, Size * 2, Size * 2);
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            var col = grid[x, y] switch { Cell.Floor => Color.SteelBlue, Cell.Edge => Color.White, Cell.Marker => Color.Orange, _ => Color.Black };
            img.SetPixel(Size * 2 + x * 2, y * 2, col); img.SetPixel(Size * 2 + x * 2 + 1, y * 2, col);
            img.SetPixel(Size * 2 + x * 2, y * 2 + 1, col); img.SetPixel(Size * 2 + x * 2 + 1, y * 2 + 1, col);
        }
        foreach (var (x, y) in path) g.FillRectangle(Brushes.Yellow, Size * 2 + x * 2, y * 2, 2, 2);
        g.FillEllipse(Brushes.Red, Size * 2 + leg.x * 2 - 5, leg.y * 2 - 5, 10, 10);
        g.DrawEllipse(Pens.Magenta, (float)(Size * 2 + goal.x * 2 - 6), (float)(goal.y * 2 - 6), 12, 12);
        img.Save(file);
    }

    public static string Ascii(Cell[,] grid, int step = 4)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < Size; y += step)
        {
            for (int x = 0; x < Size; x += step)
            {
                int floor = 0, edge = 0, marker = 0;
                for (int dy = 0; dy < step; dy++) for (int dx = 0; dx < step; dx++)
                    switch (grid[x + dx, y + dy]) { case Cell.Floor: floor++; break; case Cell.Edge: edge++; break; case Cell.Marker: marker++; break; }
                sb.Append(marker > 0 ? 'o' : edge > 1 ? '#' : floor * 2 >= step * step ? '.' : ' ');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
