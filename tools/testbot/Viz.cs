using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

// `memtool viz`: a live window for recording next to the game (picture in picture). It shows the current map's
// picture from its map package, the party's position and trail, the route the pathfinder is following right now
// (tnav's LiveState), the map's exits with where they lead, and the trip's chain of maps. It reads the position and
// map from the game itself 30 times a second, so it also works while you walk by hand. T toggles always-on-top.
sealed class VizForm : Form
{
    readonly Func<(float x, float y, float z)> _pos;
    readonly Func<int> _map;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    // current map
    int _mapId = -1;
    Bitmap? _tex; Rectangle _crop; int _ox, _oy;
    string _place = "", _script = "";
    DateTime _nextLookup;
    readonly List<(float x, float z)> _trail = new();
    (float x, float z) _at; double _heading;

    // what the walking commands are doing
    LiveState.RouteState? _route; LiveState.TripState? _trip;
    DateTime _routeTime, _tripTime;

    // static data
    readonly Dictionary<int, (string script, string place)> _names = new();
    readonly List<(int map, int dest, List<(float ax, float az, float bx, float bz)> segs)> _exits = new();

    static readonly Color Back = Color.FromArgb(11, 15, 23), Panel = Color.FromArgb(20, 27, 40), Muted = Color.FromArgb(140, 155, 180),
        Gold = Color.FromArgb(255, 196, 64), Ice = Color.FromArgb(150, 225, 255), Hero = Color.FromArgb(255, 120, 60), Bad = Color.FromArgb(255, 80, 80);
    readonly Font _title = new("Segoe UI Semibold", 20f), _small = new("Segoe UI", 10f), _chainFont = new("Segoe UI", 11f), _label = new("Segoe UI Semibold", 9.5f);

    public VizForm(Func<(float x, float y, float z)> pos, Func<int> map)
    {
        _pos = pos; _map = map;
        Text = "FFXII trip viewer";
        BackColor = Back; ForeColor = Color.White;
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        int side = Math.Min(900, work.Height - 60);
        ClientSize = new Size(side - 20, side);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.T) TopMost = !TopMost; };
        LoadStatic();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    void LoadStatic()
    {
        var ci = CultureInfo.InvariantCulture;
        if (File.Exists(Paths.MapNames))
            foreach (var f in File.ReadLines(Paths.MapNames).Skip(1).Select(l => l.Split(',')).Where(f => f.Length >= 3))
                _names[Convert.ToInt32(f[0], 16)] = (f[1], f[2]);
        if (File.Exists(Paths.Exits))
            foreach (var f in File.ReadLines(Paths.Exits).Skip(1).Select(l => l.Split(',')).Where(f => f.Length >= 11))
            {
                var segs = f[10].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(':').Select(v => float.Parse(v, ci)).ToArray())
                    .Select(v => (v[0], v[1], v[2], v[3])).ToList();
                _exits.Add((Convert.ToInt32(f[0], 16), Convert.ToInt32(f[7], 16), segs));
            }
    }

    string PlaceOf(int id) => _names.TryGetValue(id, out var n) ? (n.place.Length > 0 ? n.place : n.script) : $"map 0x{id:X}";

    void Tick()
    {
        int map = _map();
        var (x, _, z) = _pos();
        if (map != _mapId) { _mapId = map; _tex?.Dispose(); _tex = null; _trail.Clear(); _nextLookup = DateTime.MinValue; }
        if (_tex is null && DateTime.Now >= _nextLookup) { LoadMap(); _nextLookup = DateTime.Now.AddSeconds(1); }
        if (x != 0 || z != 0)
        {
            var last = _trail.Count > 0 ? _trail[^1] : (x: float.NaN, z: float.NaN);
            double d = float.IsNaN(last.x) ? 99 : Math.Sqrt((x - last.x) * (x - last.x) + (z - last.z) * (z - last.z));
            if (d > 0.15 && d < 20) { _heading = Math.Atan2(z - last.z, x - last.x); }
            if (d > 0.15) { _trail.Add((x, z)); if (_trail.Count > 6000) _trail.RemoveAt(0); }
            _at = (x, z);
        }
        Reload(LiveState.RouteFile, ref _routeTime, ref _route);
        Reload(LiveState.TripFile, ref _tripTime, ref _trip);
        Invalidate();
    }

    static void Reload<T>(string file, ref DateTime seen, ref T? value) where T : class
    {
        var t = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        if (t == seen) return;
        seen = t;
        value = LiveState.Load<T>(file) ?? value;
    }

    void LoadMap()
    {
        _script = _names.TryGetValue(_mapId, out var n) ? n.script : $"0x{_mapId:X}";
        _place = PlaceOf(_mapId);
        var cache = Path.Combine(Paths.Tex, "maps.csv");
        var row = File.Exists(cache) ? File.ReadLines(cache).FirstOrDefault(l => l.StartsWith(_mapId + ",")) : null;
        if (row is null) return;
        var f = row.Split(',');
        var file = Path.Combine(Paths.Tex, f[1] + ".png");
        if (!File.Exists(file)) return;
        _ox = int.Parse(f[2]); _oy = int.Parse(f[3]);
        (_tex, _crop) = Prepare(file);
        // widen the view to take in this map's exit lines that lie near the picture (the trigger lines sit just past the
        // drawn floor); exits far away are shown as arrows on the edge instead
        var content = _crop;
        foreach (var ex in _exits.Where(x => x.map == _mapId))
            foreach (var sg in ex.segs)
                foreach (var (wx, wz) in new[] { (sg.ax, sg.az), (sg.bx, sg.bz) })
                {
                    int u = (int)(wx * 4 + _ox), v = (int)(wz * 4 + _oy);
                    if (u > content.Left - 160 && u < content.Right + 160 && v > content.Top - 160 && v < content.Bottom + 160)
                        _crop = Rectangle.Union(_crop, Rectangle.FromLTRB(u - 40, v - 50, u + 40, v + 30));
                }
    }

    // The package picture with its black outside made transparent, and the box around what's drawn.
    static (Bitmap, Rectangle) Prepare(string file)
    {
        using var src = new Bitmap(file);
        var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
        int x0 = bmp.Width, y0 = bmp.Height, x1 = 0, y1 = 0;
        for (int i = 0; i < px.Length; i++)
        {
            var c = Color.FromArgb(px[i]);
            int lum = Math.Max(c.R, Math.Max(c.G, c.B));
            int a = Math.Min(c.A, Math.Min(255, lum * 3));
            // the package marks floor in pure red (a mask colour); show it as the in-game map's blue, keep the wall glow
            if (c.R > c.G + 60 && c.R > c.B + 60)
            {
                double k = c.R / 255.0;
                c = Color.FromArgb((int)(34 * k), (int)(70 * k), (int)(122 * k));
            }
            px[i] = Color.FromArgb(a, c.R, c.G, c.B).ToArgb();
            if (a > 40) { int x = i % bmp.Width, y = i / bmp.Width; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        }
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        var crop = x1 > x0 ? Rectangle.FromLTRB(Math.Max(0, x0 - 12), Math.Max(0, y0 - 12), Math.Min(bmp.Width, x1 + 12), Math.Min(bmp.Height, y1 + 12)) : new Rectangle(0, 0, bmp.Width, bmp.Height);
        return (bmp, crop);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Back);

        int header = DrawHeader(g);
        var area = new Rectangle(16, header + 8, ClientSize.Width - 32, ClientSize.Height - header - 24);
        if (_tex is null)
        {
            TextRenderer.DrawText(g, "Finding this map's picture...", _chainFont, area, Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        double s = Math.Min(area.Width / (double)_crop.Width, area.Height / (double)_crop.Height);
        double offX = area.X + (area.Width - _crop.Width * s) / 2, offY = area.Y + (area.Height - _crop.Height * s) / 2;
        PointF P(double wx, double wz) => new((float)(offX + (wx * 4 + _ox - _crop.X) * s), (float)(offY + (wz * 4 + _oy - _crop.Y) * s));

        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.SetClip(area);
        g.DrawImage(_tex, new RectangleF((float)(offX - _crop.X * s), (float)(offY - _crop.Y * s), (float)(_tex.Width * s), (float)(_tex.Height * s)));

        // exits of this map, labelled with where they lead; the one the trip is heading for in gold
        int next = _trip is { } tr && tr.ChainIds.Length > tr.Current && tr.Current >= 0 && tr.ChainIds[tr.Current] == _mapId ? tr.NextMap : -1;
        foreach (var ex in _exits.Where(x => x.map == _mapId).GroupBy(x => x.dest).Select(gr => gr.First()))
        {
            bool target = ex.dest == next;
            using var pen = new Pen(target ? Gold : Color.FromArgb(200, Ice), target ? 6f : 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (var sg in ex.segs) g.DrawLine(pen, P(sg.ax, sg.az), P(sg.bx, sg.bz));
            var mid = P(ex.segs.Average(q => (q.ax + q.bx) / 2), ex.segs.Average(q => (q.az + q.bz) / 2));
            string text = (target ? "Next: " : "") + PlaceOf(ex.dest);
            if (area.Contains(Point.Round(mid))) Label(g, text, mid, target ? Gold : Ice);
            else EdgeArrow(g, area, mid, text, target ? Gold : Ice);
        }

        // trail: where the party has been on this map
        if (_trail.Count > 1)
        {
            var pts = _trail.Select(t => P(t.x, t.z)).ToArray();
            using var trail = new Pen(Color.FromArgb(150, 210, 230, 255), 2.5f) { LineJoin = LineJoin.Round, DashStyle = DashStyle.Dot };
            g.DrawLines(trail, pts);
        }

        // the route the pathfinder is following now, from the party onwards
        if (_route is { } r && r.Map == _mapId && r.Route.Length > 1)
        {
            int near = 0; double best = double.MaxValue;
            for (int i = 0; i < r.Route.Length; i++)
            {
                double d = (r.Route[i][0] - _at.x) * (r.Route[i][0] - _at.x) + (r.Route[i][1] - _at.z) * (r.Route[i][1] - _at.z);
                if (d < best) { best = d; near = i; }
            }
            var ahead = new List<PointF> { P(_at.x, _at.z) };
            for (int i = near; i < r.Route.Length; i += 2) ahead.Add(P(r.Route[i][0], r.Route[i][1]));
            ahead.Add(P(r.Route[^1][0], r.Route[^1][1]));
            if (ahead.Count > 1)
            {
                using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 9f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var line = new Pen(Gold, 4.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(shadow, ahead.ToArray());
                g.DrawLines(line, ahead.ToArray());
            }
            var goal = P(r.Goal[0], r.Goal[1]);
            using (var gb = new SolidBrush(Gold)) g.FillEllipse(gb, goal.X - 7, goal.Y - 7, 14, 14);
            using (var gp = new Pen(Color.Black, 2f)) g.DrawEllipse(gp, goal.X - 7, goal.Y - 7, 14, 14);
            foreach (var b in r.Blocks)
            {
                var bp = P(b[0], b[1]); float rad = (float)(2 * 4 * s);
                using var bpen = new Pen(Bad, 3f);
                g.DrawEllipse(bpen, bp.X - rad, bp.Y - rad, rad * 2, rad * 2);
                g.DrawLine(bpen, bp.X - rad * 0.6f, bp.Y - rad * 0.6f, bp.X + rad * 0.6f, bp.Y + rad * 0.6f);
                g.DrawLine(bpen, bp.X - rad * 0.6f, bp.Y + rad * 0.6f, bp.X + rad * 0.6f, bp.Y - rad * 0.6f);
            }
        }

        // the party
        var at = P(_at.x, _at.z);
        var tip = new PointF(at.X + (float)Math.Cos(_heading) * 20, at.Y + (float)Math.Sin(_heading) * 20);
        var l = new PointF(at.X + (float)Math.Cos(_heading + 2.5) * 10, at.Y + (float)Math.Sin(_heading + 2.5) * 10);
        var rr = new PointF(at.X + (float)Math.Cos(_heading - 2.5) * 10, at.Y + (float)Math.Sin(_heading - 2.5) * 10);
        using (var hb = new SolidBrush(Hero)) g.FillPolygon(hb, new[] { tip, l, at, rr });
        using (var hb = new SolidBrush(Hero)) g.FillEllipse(hb, at.X - 9, at.Y - 9, 18, 18);
        using (var hp = new Pen(Color.White, 2.5f)) g.DrawEllipse(hp, at.X - 9, at.Y - 9, 18, 18);

        g.ResetClip();
        TextRenderer.DrawText(g, $"({_at.x:F1}, {_at.z:F1})", _small, new Point(area.X, ClientSize.Height - 22), Muted);
    }

    // Place name, status and the trip's chain of maps; returns the header height.
    int DrawHeader(Graphics g)
    {
        int w = ClientSize.Width;
        using (var pb = new SolidBrush(Panel)) g.FillRectangle(pb, 0, 0, w, 92);
        TextRenderer.DrawText(g, _place, _title, new Point(14, 8), Color.White);
        var titleW = TextRenderer.MeasureText(_place, _title).Width;
        TextRenderer.DrawText(g, _script, _small, new Point(14 + titleW + 4, 20), Muted);
        // the trip's step, plus the walk's own state when it says more (going around a wall, stuck)
        string status = _trip?.Status ?? "";
        foreach (var (script, place) in _names.Values) if (place.Length > 0 && status.Contains(script)) status = status.Replace(script, place);
        if (_route is { } r && r.Map == _mapId && r.Status is not ("walking" or "arrived" or "map changed"))
            status = status.Length > 0 ? $"{status} · {r.Status}" : r.Status;
        TextRenderer.DrawText(g, status, _small, new Rectangle(w - 380, 16, 366, 24), status.StartsWith("stuck") || status.StartsWith("stopped") ? Bad : Gold,
            TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

        // chain: done maps muted, the current one gold, the rest white
        int x = 14, y = 58;
        if (_trip is { } t && t.Chain.Length > 0)
        {
            // too long for the window: drop maps already walked from the front (shown as "...") until the current one
            // and the rest fit
            int Width(int i) => TextRenderer.MeasureText(PlaceOf(t.ChainIds[i]), _chainFont).Width + 16;
            int first = 0, ell = TextRenderer.MeasureText("... ›", _chainFont).Width + 4;
            while (first < Math.Max(0, t.Current) && (first > 0 ? ell : 0) + Enumerable.Range(first, t.Chain.Length - first).Sum(Width) > w - 28) first++;
            if (first > 0) { TextRenderer.DrawText(g, "... ›", _chainFont, new Point(x, y), Muted); x += ell; }
            for (int i = first; i < t.Chain.Length; i++)
            {
                string label = PlaceOf(t.ChainIds[i]);
                var c = i < t.Current ? Muted : i == t.Current ? Gold : Color.White;
                int width = TextRenderer.MeasureText(label, _chainFont).Width;
                if (x + width > w - 14) { TextRenderer.DrawText(g, "...", _chainFont, new Point(x, y), Muted); break; }
                TextRenderer.DrawText(g, label, _chainFont, new Point(x, y), c);
                x += width;
                if (i < t.Chain.Length - 1) { TextRenderer.DrawText(g, "›", _chainFont, new Point(x, y), Muted); x += 16; }
            }
        }
        else TextRenderer.DrawText(g, "No trip running", _chainFont, new Point(x, y), Muted);
        return 92;
    }

    // An exit outside the view: an arrow on the view's edge pointing at it, with its label.
    void EdgeArrow(Graphics g, Rectangle area, PointF target, string text, Color color)
    {
        var c = new PointF(area.X + area.Width / 2f, area.Y + area.Height / 2f);
        double dx = target.X - c.X, dy = target.Y - c.Y;
        double t = Math.Min((area.Width / 2.0 - 18) / Math.Max(1e-6, Math.Abs(dx)), (area.Height / 2.0 - 18) / Math.Max(1e-6, Math.Abs(dy)));
        var at = new PointF((float)(c.X + dx * t), (float)(c.Y + dy * t));
        double a = Math.Atan2(dy, dx);
        PointF Pt(double ang, double r) => new((float)(at.X + Math.Cos(ang) * r), (float)(at.Y + Math.Sin(ang) * r));
        using (var b = new SolidBrush(color)) g.FillPolygon(b, new[] { Pt(a, 14), Pt(a + 2.4, 11), Pt(a - 2.4, 11) });
        var size = TextRenderer.MeasureText(text, _label);
        var lp = new PointF(at.X - (float)Math.Cos(a) * (size.Width / 2f + 20), at.Y - (float)Math.Sin(a) * (size.Height + 14) + size.Height);
        Label(g, text, lp, color);
    }

    void Label(Graphics g, string text, PointF at, Color color)
    {
        var size = TextRenderer.MeasureText(text, _label);
        var box = new Rectangle((int)(at.X - size.Width / 2) , (int)(at.Y - size.Height - 10), size.Width + 4, size.Height + 2);
        box.X = Math.Clamp(box.X, 4, ClientSize.Width - box.Width - 4);
        box.Y = Math.Clamp(box.Y, 96, ClientSize.Height - box.Height - 4);
        using (var b = new SolidBrush(Color.FromArgb(200, 8, 12, 20))) g.FillRectangle(b, box);
        TextRenderer.DrawText(g, text, _label, box, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public static void Run(Func<(float x, float y, float z)> pos, Func<int> map)
    {
        var t = new Thread(() => { Application.EnableVisualStyles(); Application.SetHighDpiMode(HighDpiMode.SystemAware); Application.Run(new VizForm(pos, map)); });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }
}
