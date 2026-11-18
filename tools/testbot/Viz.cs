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
    readonly Func<IReadOnlySet<int>> _touched; // key items of the touched teleport crystals (the game's destination lines)
    Ff12MapData.Crystal? _crystal; // this map's teleport crystal
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

    // current map's exits, from the map reader: where each leads and its trigger line
    List<(int dest, List<(double ax, double az, double bx, double bz)> segs)> _exits = new();
    int _variant = -1, _loading = -1;
    DateTime _retryAt;

    static readonly Color Back = Color.FromArgb(11, 15, 23), Panel = Color.FromArgb(20, 27, 40), Muted = Color.FromArgb(140, 155, 180),
        Gold = Color.FromArgb(255, 196, 64), Ice = Color.FromArgb(150, 225, 255), Hero = Color.FromArgb(255, 120, 60), Bad = Color.FromArgb(255, 80, 80);
    readonly Font _title = new("Segoe UI Semibold", 20f), _small = new("Segoe UI", 10f), _chainFont = new("Segoe UI", 11f), _label = new("Segoe UI Semibold", 9.5f);

    public VizForm(Func<(float x, float y, float z)> pos, Func<int> map, Func<IReadOnlySet<int>> touched)
    {
        _pos = pos; _map = map; _touched = touched;
        Text = "FFXII trip viewer";
        BackColor = Back; ForeColor = Color.White;
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        int side = Math.Min(900, work.Height - 60);
        ClientSize = new Size(side - 20, side);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        // stays above the game without taking focus: the bot's key presses must keep going to the game. T toggles it.
        TopMost = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.T) TopMost = !TopMost; };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    static string PlaceOf(int id) => Ff12MapData.GameFiles.PlaceName(id);

    void Tick()
    {
        int map = _map();
        var (x, _, z) = _pos();
        Reload(LiveState.RouteFile, ref _routeTime, ref _route);
        Reload(LiveState.TripFile, ref _tripTime, ref _trip);
        // show the minimap variant the walk is using on this map (variant 0 until a walk says otherwise)
        int wantVariant = _route is { } rt && rt.Map == map ? rt.Variant : 0;
        if (map != _mapId) { _mapId = map; _tex?.Dispose(); _tex = null; _variant = -1; _trail.Clear(); _exits = new(); }
        if (_variant != wantVariant && _loading < 0) LoadMap(map, wantVariant);
        if (x != 0 || z != 0)
        {
            var last = _trail.Count > 0 ? _trail[^1] : (x: float.NaN, z: float.NaN);
            double d = float.IsNaN(last.x) ? 99 : Math.Sqrt((x - last.x) * (x - last.x) + (z - last.z) * (z - last.z));
            if (d > 0.15 && d < 20) { _heading = Math.Atan2(z - last.z, x - last.x); }
            if (d > 20) _trail.Clear(); // a jump (teleport, or a stale position right after a map change) starts a new trail
            if (d > 0.15) { _trail.Add((x, z)); if (_trail.Count > 6000) _trail.RemoveAt(0); }
            _at = (x, z);
        }
        Invalidate();
    }

    static void Reload<T>(string file, ref DateTime seen, ref T? value) where T : class
    {
        var t = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        if (t == seen) return;
        seen = t;
        value = LiveState.Load<T>(file) ?? value;
    }

    // Reads the map from the game's files off the window's thread (the first read of an area takes a few seconds).
    void LoadMap(int map, int variant)
    {
        if (DateTime.UtcNow < _retryAt) return;
        _loading = map;
        Task.Run(() =>
        {
            try { Load(); }
            catch (Exception e)
            {
                // a failed read (say a cache file another bot process is writing at the same moment) must not stop the
                // window from loading maps: try again in a second
                Console.WriteLine($"viz: reading map 0x{map:X} failed ({e.GetType().Name}: {e.Message}); retrying");
                BeginInvoke(() => { _loading = -1; _retryAt = DateTime.UtcNow.AddSeconds(1); });
            }
        });
        void Load()
        {
            var info = Ff12MapData.MapInfo.For(map);
            var crystal = Ff12MapData.Crystals.On(map);
            var exits = info?.Info.Exits.SelectMany(x => x.Leads.Select(l => (l.Map, x.Line.Select(s => (s.A.X, s.A.Z, s.B.X, s.B.Z)).ToList()))).ToList() ?? new();
            Bitmap? tex = null; Rectangle crop = default; int ox = 0, oy = 0;
            if (info?.Package is { VariantCount: > 0 } pkg)
            {
                ox = (int)Math.Round(-pkg.Origin.X * 4); oy = (int)Math.Round(-pkg.Origin.Z * 4);
                lock (pkg) (tex, crop) = Prepare(pkg.Minimap(Math.Min(variant, pkg.VariantCount - 1)));
                // widen the view to take in this map's exit lines that lie near the picture (the trigger lines sit just
                // past the drawn floor); exits far away are shown as arrows on the edge instead
                var content = crop;
                foreach (var (_, segs) in exits)
                    foreach (var sg in segs)
                        foreach (var (wx, wz) in new[] { (sg.Item1, sg.Item2), (sg.Item3, sg.Item4) })
                        {
                            int u = (int)(wx * 4 + ox), v = (int)(wz * 4 + oy);
                            if (u > content.Left - 160 && u < content.Right + 160 && v > content.Top - 160 && v < content.Bottom + 160)
                                crop = Rectangle.Union(crop, Rectangle.FromLTRB(u - 40, v - 50, u + 40, v + 30));
                        }
            }
            BeginInvoke(() =>
            {
                _loading = -1;
                if (map != _mapId) { tex?.Dispose(); return; }
                _tex?.Dispose();
                _tex = tex; _crop = crop; _ox = ox; _oy = oy; _variant = variant; _exits = exits;
                _crystal = crystal; _place = PlaceOf(map); _script = (info?.Script ?? $"0x{map:X}") + (info?.Package is { VariantCount: > 1 } p ? $" · variant {variant}" : "");
            });
        }
    }

    // The package picture with its black outside made transparent, and the box around what's drawn.
    static (Bitmap, Rectangle) Prepare(Bitmap src)
    {
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
        foreach (var ex in _exits.GroupBy(x => x.dest).Select(gr => gr.First()))
        {
            bool target = ex.dest == next;
            using var pen = new Pen(target ? Gold : Color.FromArgb(200, Ice), target ? 6f : 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (var sg in ex.segs) g.DrawLine(pen, P(sg.ax, sg.az), P(sg.bx, sg.bz));
            var mid = P(ex.segs.Average(q => (q.ax + q.bx) / 2), ex.segs.Average(q => (q.az + q.bz) / 2));
            string text = (target ? "Next: " : "") + PlaceOf(ex.dest);
            if (area.Contains(Point.Round(mid))) Label(g, text, mid, target ? Gold : Ice);
            else EdgeArrow(g, area, mid, text, target ? Gold : Ice);
        }

        // the map's teleport crystal: bright when touched (teleports can go there), dim when not, gold when the trip is
        // about to teleport from it
        if (_crystal is { } cr)
        {
            bool touchedHere = _touched().Contains(cr.KeyItem);
            bool using_ = _trip is { } tc && tc.Status.StartsWith("teleporting") && tc.ChainIds.Length > tc.Current && tc.ChainIds[tc.Current] == _mapId;
            var col = using_ ? Gold : touchedHere ? Color.FromArgb(120, 210, 255) : Color.FromArgb(120, 140, 160);
            var cp = P(cr.CrystalX, cr.CrystalZ);
            float gr = 13;
            var gem = new[] { new PointF(cp.X, cp.Y - gr * 1.5f), new PointF(cp.X + gr * 0.75f, cp.Y), new PointF(cp.X, cp.Y + gr * 1.5f), new PointF(cp.X - gr * 0.75f, cp.Y) };
            using (var glow = new SolidBrush(Color.FromArgb(touchedHere || using_ ? 70 : 30, col))) g.FillEllipse(glow, cp.X - gr * 1.8f, cp.Y - gr * 1.8f, gr * 3.6f, gr * 3.6f);
            using (var fill = new LinearGradientBrush(new PointF(cp.X, cp.Y - gr * 1.5f), new PointF(cp.X, cp.Y + gr * 1.5f), Color.White, col)) g.FillPolygon(fill, gem);
            using (var edge = new Pen(Color.FromArgb(200, 10, 20, 35), 1.5f)) g.DrawPolygon(edge, gem);
            Label(g, using_ ? "Teleporting" : touchedHere ? "Crystal" : "Crystal (not touched)", new PointF(cp.X, cp.Y + gr * 1.6f + 22), col);
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

    protected override bool ShowWithoutActivation => true;

    /// <summary>Opens the viewer on a monitor the game isn't on (the game's borderless window covers its own monitor), or
    /// beside the game when there is only one.</summary>
    public static void Run(Func<(float x, float y, float z)> pos, Func<int> map, Func<IReadOnlySet<int>> touched, IntPtr gameWindow)
    {
        var t = new Thread(() =>
        {
            Application.EnableVisualStyles(); Application.SetHighDpiMode(HighDpiMode.SystemAware);
            var form = new VizForm(pos, map, touched) { StartPosition = FormStartPosition.Manual };
            var game = Screen.FromHandle(gameWindow);
            var screen = Screen.AllScreens.FirstOrDefault(s => !s.Equals(game)) ?? game;
            form.Location = new Point(screen.WorkingArea.X + 40, screen.WorkingArea.Y + 20);
            Application.Run(form);
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }
}
