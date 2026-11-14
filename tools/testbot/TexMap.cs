using System.Drawing;
using System.Runtime.InteropServices;

// Whole-map navigation on a map package's minimap texture (red band = floor, 4 px per world unit).
sealed class TexMap
{
    public readonly int W, H;
    public readonly bool[,] Floor;
    public readonly int[,] Clear; // distance in px to the nearest non-floor pixel (capped)
    public int Ox, Oy;
    public readonly float[,] Edge;

    public TexMap(string file, int ox, int oy)
    {
        Ox = ox; Oy = oy;
        using (var eb = new Bitmap(file)) Edge = Minimap.EdgeMap(eb);
        using var bmp = new Bitmap(file);
        W = bmp.Width; H = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[W * H];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        Floor = new bool[W, H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = Color.FromArgb(px[y * W + x]);
                Floor[x, y] = c.A > 100 && c.R > 140 && c.G < 130 && c.B < 130;
            }
        Clear = new int[W, H];
        ComputeClear();
    }

    void ComputeClear()
    {
        var q = new Queue<(int, int)>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            if (!Floor[x, y]) { Clear[x, y] = 0; q.Enqueue((x, y)); } else Clear[x, y] = int.MaxValue;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (nx >= 0 && ny >= 0 && nx < W && ny < H && Clear[nx, ny] > Clear[x, y] + 1) { Clear[nx, ny] = Clear[x, y] + 1; q.Enqueue((nx, ny)); }
        }
    }

    // A wall the texture doesn't show (barrier, closed gate): removes a disc of floor and refreshes clearances.
    public void AddBlock(double u, double v, int r)
    {
        for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
        {
            int x = (int)u + dx, y = (int)v + dy;
            if (x >= 0 && y >= 0 && x < W && y < H && dx * dx + dy * dy <= r * r) Floor[x, y] = false;
        }
        ComputeClear();
    }

    public (double u, double v) ToPx(double wx, double wz) => (wx * 4 + Ox, wz * 4 + Oy);
    public (double x, double z) ToWorld(double u, double v) => ((u - Ox) / 4, (v - Oy) / 4);

    (int, int) Nearest(int x, int y)
    {
        // nearest floor pixel (the party can stand slightly outside the drawn band)
        x = Math.Clamp(x, 0, W - 1); y = Math.Clamp(y, 0, H - 1);
        for (int r = 0; r < 300; r++)
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                int ax = x + dx, ay = y + dy;
                if (ax >= 0 && ay >= 0 && ax < W && ay < H && Floor[ax, ay]) return (ax, ay);
            }
        return (x, y);
    }

    // A* over floor pixels, preferring the middle of corridors. Returns the pixel path, start first; empty if unreachable.
    public List<(int x, int y)> Path((int x, int y) from, (int x, int y) to)
    {
        var s = Nearest(from.x, from.y); var g = Nearest(to.x, to.y);
        var dist = new float[W, H]; var prev = new int[W, H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) dist[x, y] = float.MaxValue;
        var pq = new PriorityQueue<(int x, int y), float>();
        dist[s.Item1, s.Item2] = 0; pq.Enqueue(s, 0);
        while (pq.TryDequeue(out var c, out float pri))
        {
            if (c == g) break;
            float d = dist[c.x, c.y];
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = c.x + dx, ny = c.y + dy;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H || !Floor[nx, ny]) continue;
                float step = (dx != 0 && dy != 0 ? 1.414f : 1f) * (1 + 12f / Math.Min(Clear[nx, ny], 10));
                if (d + step < dist[nx, ny])
                {
                    dist[nx, ny] = d + step; prev[nx, ny] = c.y * W + c.x;
                    float h = MathF.Sqrt((nx - g.Item1) * (nx - g.Item1) + (ny - g.Item2) * (ny - g.Item2)) * 2.2f;
                    pq.Enqueue((nx, ny), d + step + h);
                }
            }
        }
        var path = new List<(int, int)>();
        if (dist[g.Item1, g.Item2] == float.MaxValue) return path;
        for (var c = g; c != s; c = (prev[c.Item1, c.Item2] % W, prev[c.Item1, c.Item2] / W)) path.Add(c);
        path.Add(s); path.Reverse();
        return path;
    }

    public void SaveDebug(string file, List<(int x, int y)> path, (double u, double v) at, (int x, int y) next)
    {
        int x0 = W, y0 = H, x1 = 0, y1 = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (Floor[x, y]) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        x0 = Math.Max(0, x0 - 10); y0 = Math.Max(0, y0 - 10); x1 = Math.Min(W - 1, x1 + 10); y1 = Math.Min(H - 1, y1 + 10);
        using var img = new Bitmap(x1 - x0 + 1, y1 - y0 + 1);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) img.SetPixel(x - x0, y - y0, Floor[x, y] ? Color.SteelBlue : Color.Black);
        using (var gr = Graphics.FromImage(img))
        {
            foreach (var (x, y) in path) gr.FillRectangle(Brushes.Yellow, x - x0, y - y0, 2, 2);
            gr.FillEllipse(Brushes.Orange, (float)(at.u - x0 - 5), (float)(at.v - y0 - 5), 10, 10);
            gr.FillEllipse(Brushes.Red, next.x - x0 - 4, next.y - y0 - 4, 8, 8);
        }
        img.Save(file);
    }
}
