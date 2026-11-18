#:property TargetFramework=net10.0-windows
#:project ../MapReader/MapReader.csproj
using Ff12MapData;

// Front end for the map-data reader (tools/MapReader). Everything is read from the game's files on demand.
//   dotnet run mapdata.cs -- map <name|hexid>            what a map's data says: package, entrances, exits, barriers,
//                                                          minimap variants and what switches them
//   dotnet run mapdata.cs -- route <from> <to>           fewest-maps route over the game's own exits
//   dotnet run mapdata.cs -- explain <map> <x> <z>       why the party might be stuck at a spot
//   dotnet run mapdata.cs -- scan                         every map: barriers and variant changes (slow the first time)
//   dotnet run mapdata.cs -- draw <map> <out.png> [variant]   picture: minimap, walk mesh, exits, barriers, variant changes
//   dotnet run mapdata.cs -- actor <map> <actor>         one actor's script code (call names from work/ff12-script)
//   dotnet run mapdata.cs -- crystals                     every teleport destination (number, map, landing spot)
//   dotnet run mapdata.cs -- reach <map>... | all         exits the walk mesh says can't be walked to, per entrance
//   dotnet run mapdata.cs -- packages <map>              the area's map packages: entrances on their floor, floor pieces
if (args.Length == 0) { Console.WriteLine("map <map> | route <from> <to> | explain <map> <x> <z> | scan | draw <map> <png> [variant] | actor <map> <actor>"); return 1; }
int Id(string s) => GameFiles.MapId(s) ?? Convert.ToInt32(s, 16);
string Label(int id) => $"{GameFiles.PlaceName(id)} ({GameFiles.ScriptName(id)}, 0x{id:X})";
var ci = System.Globalization.CultureInfo.InvariantCulture;

switch (args[0])
{
    case "map":
        {
            var m = MapInfo.For(Id(args[1])) ?? throw new Exception("no script for that map");
            Console.WriteLine($"{Label(m.Id)}, package {m.Package?.Name ?? "none found"}{(m.Package is { } p ? $", {p.VariantCount} minimap variant(s)" : "")}");
            for (int e = 0; e < m.Info.Entrances.Count; e++) Console.WriteLine($"  entrance {e}: ({m.Info.Entrances[e].X:F1}, {m.Info.Entrances[e].Z:F1}) facing {m.Info.Entrances[e].Facing:F2}");
            foreach (var x in m.Info.Exits)
                Console.WriteLine($"  exit at ({x.Mid.X:F1}, {x.Mid.Z:F1}) -> {string.Join(" or ", x.Leads.Select(l => $"{GameFiles.PlaceName(l.Map)} entrance {l.Entrance}"))}");
            foreach (var b in m.Barriers) Console.WriteLine($"  barrier crossing (condition {b.Tag}): ({b.Line.A.X:F1}, {b.Line.A.Z:F1})-({b.Line.B.X:F1}, {b.Line.B.Z:F1})");
            foreach (var c in m.VariantChanges)
                Console.WriteLine($"  variant {c.ToVariant} {(c.Opens ? "adds" : "removes")} floor at ({c.Min.X:F0}, {c.Min.Z:F0})-({c.Max.X:F0}, {c.Max.Z:F0}): {m.Switches(c.ToVariant)}");
            foreach (var s in m.Info.VariantSwitches.Where(s => !m.VariantChanges.Any(c => s.Variants.Contains(c.ToVariant))))
                Console.WriteLine($"  {s.Actor} switches to variant(s) {string.Join(" ", s.Variants)} (no floor change found)");
            return 0;
        }
    case "route":
        {
            int from = Id(args[1]), to = Id(args[2]);
            var prev = new Dictionary<int, (int map, Exit exit, int entrance)> { [from] = (-1, null!, -1) };
            var q = new Queue<int>(); q.Enqueue(from);
            while (q.Count > 0 && !prev.ContainsKey(to))
            {
                int cur = q.Dequeue();
                if (MapInfo.For(cur) is not { } m) continue;
                foreach (var x in m.Info.Exits) foreach (var (dest, ent) in x.Leads) if (prev.TryAdd(dest, (cur, x, ent))) q.Enqueue(dest);
            }
            if (!prev.ContainsKey(to)) { Console.WriteLine("no route"); return 1; }
            var path = new List<int>(); for (int c = to; c != from; c = prev[c].map) path.Add(c); path.Reverse();
            Console.WriteLine($"{Label(from)} -> {Label(to)}: {path.Count} map changes");
            foreach (var c in path) Console.WriteLine($"  on {GameFiles.PlaceName(prev[c].map)}: exit at ({prev[c].exit.Mid.X:F1}, {prev[c].exit.Mid.Z:F1}) -> {GameFiles.PlaceName(c)} entrance {prev[c].entrance}");
            return 0;
        }
    case "packages":
        {
            // every map package of the map's area: how many of the map's entrances lie on its walk mesh, and its pieces
            var m = MapInfo.For(Id(args[1])) ?? throw new Exception("no script for that map");
            var ents = m.Info.Entrances.Where(e => e.X != 0 || e.Z != 0).Select(e => new Point2(e.X, e.Z)).ToList();
            foreach (var name in MapPackage.InArea(m.Script[..3]))
                if (MapPackage.Get(name) is { Mesh: { } mesh } && ents.Count(p => mesh.TriangleAt(p) >= 0) is int hits and > 0)
                    Console.WriteLine($"  {name}: {hits}/{ents.Count} entrances on its floor, {Enumerable.Range(0, mesh.Triangles.Length).Select(mesh.PieceOf).Distinct().Count()} piece(s), entrance pieces {string.Join(" ", ents.Select(p => mesh.TriangleAt(p) is int t and >= 0 ? mesh.PieceOf(t).ToString() : "-"))}");
            return 0;
        }
    case "reach":
        {
            // reach <map>...: for each entrance, the exits the walk mesh says can't be walked to from it
            foreach (var a in args[1] == "all" ? GameFiles.AllMapIds.Select(i => i.ToString("X")) : args.Skip(1))
            {
                if (MapInfo.For(Id(a)) is not { } m) continue;
                if (m.Package?.Mesh is null || m.Info.Exits.Count == 0) continue; Console.WriteLine(Label(m.Id));
                for (int e = 0; e < m.Info.Entrances.Count; e++)
                {
                    var (ex, _, ez, _) = m.Info.Entrances[e]; if (ex == 0 && ez == 0) continue;
                    var cut = m.Info.Exits.Where(x => !m.CanWalkTo(new(ex, ez), x)).ToList();
                    if (cut.Count > 0) Console.WriteLine($"  from entrance {e}: can't reach {string.Join(", ", cut.Select(x => $"{string.Join("/", x.Leads.Select(l => GameFiles.PlaceName(l.Map)).Distinct())} ({x.Mid.X:F0}, {x.Mid.Z:F0})"))}");
                }
            }
            return 0;
        }
    case "explain":
        {
            var m = MapInfo.For(Id(args[1])) ?? throw new Exception("no script for that map");
            var at = new Point2(double.Parse(args[2], ci), double.Parse(args[3], ci));
            Console.WriteLine($"{Label(m.Id)} at ({at.X:F1}, {at.Z:F1}):");
            foreach (var line in m.Explain(at)) Console.WriteLine("  " + line);
            return 0;
        }
    case "draw":
        {
            // draw <map> <out.png> [variant]: the map's minimap picture with its walk mesh, exits (cyan), conditional
            // crossings (red) and variant changes (yellow boxes), twice the picture's size
            var m = MapInfo.For(Id(args[1])) ?? throw new Exception("no script for that map");
            if (m.Package is not { VariantCount: > 0, Mesh: { } mesh } pkg) throw new Exception("no package with a picture and a walk mesh");
            int variant = args.Length > 3 ? int.Parse(args[3]) : 0;
            var pic = pkg.Minimap(variant);
            using var bmp = new System.Drawing.Bitmap(pic.Width * 2, pic.Height * 2);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.DrawImage(pic, 0, 0, bmp.Width, bmp.Height);
            System.Drawing.PointF P(Point2 w) { var (u, v) = pkg.ToPixel(w); return new((float)u * 2, (float)v * 2); }
            using (var tri = new System.Drawing.Pen(System.Drawing.Color.FromArgb(110, 255, 255, 255)))
                foreach (var (a, b, c, _) in mesh.Triangles)
                    if (a < mesh.Vertices.Length && b < mesh.Vertices.Length && c < mesh.Vertices.Length)
                        g.DrawPolygon(tri, [P(mesh.Vertices[a]), P(mesh.Vertices[b]), P(mesh.Vertices[c])]);
            using (var ex = new System.Drawing.Pen(System.Drawing.Color.Cyan, 5))
                foreach (var x in m.Info.Exits) foreach (var s in x.Line) g.DrawLine(ex, P(s.A), P(s.B));
            using (var bar = new System.Drawing.Pen(System.Drawing.Color.Red, 5))
                foreach (var b in m.Barriers) g.DrawLine(bar, P(b.Line.A), P(b.Line.B));
            using (var ch = new System.Drawing.Pen(System.Drawing.Color.Yellow, 3))
                foreach (var c in m.VariantChanges) { var a = P(c.Min); var b = P(c.Max); g.DrawRectangle(ch, a.X, a.Y, b.X - a.X, b.Y - a.Y); }
            bmp.Save(args[2]);
            Console.WriteLine($"saved {args[2]}");
            return 0;
        }
    case "actor":
        {
            // actor <map> <actor name>: one actor's script code. Call names come from ff12-script's table when it is
            // checked out in work/ff12-script; otherwise calls show as numbers.
            var name = GameFiles.ScriptName(Id(args[1]))!;
            var bytes = GameFiles.Get($"ps2data/plan_master/us/plan_map/{name[..5]}/{name}/global/{name}.ebp") ?? throw new Exception("no script");
            var calls = new Dictionary<int, string>();
            var table = Path.Combine(GameFiles.Repo, "work", "ff12-script", "ff12-script", "CScriptContainerInitializers.cpp");
            if (File.Exists(table))
                foreach (System.Text.RegularExpressions.Match c in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(table), @"\{ 0x([0-9a-fA-F]{4}), \{ L""([^""]+)"""))
                    calls.TryAdd(Convert.ToInt32(c.Groups[1].Value, 16), c.Groups[2].Value);
            var s = new Ff12Randomizer.Core.GameData.MapScript(bytes);
            var a = s.Actors.First(x => x.Name == args[2]);
            var ops = new Dictionary<byte, string> { [0x48] = "PUSHV", [0x49] = "POPV", [0x4F] = "PUSHII", [0x51] = "JMP", [0x5C] = "JZ", [0x5D] = "CALL", [0x58] = "CALLX",
                [0x0A] = "==", [0x0B] = "!=", [0x0C] = ">", [0x0D] = "<", [0x0E] = ">=", [0x0F] = "<=", [0x2D] = "RET" };
            foreach (var x in s.Instructions(a))
            {
                string op = ops.TryGetValue(x.Op, out var n) ? n : $"op{x.Op:X2}";
                string arg = x.Op < 0x48 ? "" : x.Op is 0x5D or 0x58 ? calls.GetValueOrDefault(x.Operand, $"0x{x.Operand:X}")
                    : x.Op is 0x48 or 0x49 && x.Operand == s.StoryFlag ? "STORYFLAG" : $"{(short)x.Operand}";
                Console.WriteLine($"  {x.At:X5} {op} {arg}");
            }
            return 0;
        }
    case "crystals":
        {
            // crystals: every teleport destination: its number in the teleport list, map, and where you land (by the crystal)
            foreach (var c in Crystals.List) Console.WriteLine($"  0x{c.KeyItem:X4}: {Label(c.Map)} entrance {c.Entrance} at ({c.X:F1}, {c.Z:F1})");
            Console.WriteLine($"{Crystals.List.Count} teleport destinations");
            return 0;
        }
    case "scan":
        {
            int maps = 0, withBarriers = 0, withVariants = 0;
            foreach (int id in GameFiles.AllMapIds)
            {
                if (MapInfo.For(id) is not { } m) continue;
                maps++;
                if (m.Barriers.Count > 0) withBarriers++;
                if (m.VariantChanges.Count > 0) withVariants++;
                if (m.Barriers.Count > 0 || m.VariantChanges.Count > 0)
                    Console.WriteLine($"{Label(id)}: {m.Barriers.Count} barrier crossing(s), {m.VariantChanges.Count} variant change(s)");
            }
            Console.WriteLine($"{maps} maps: {withBarriers} with barrier crossings, {withVariants} with floor that changes between minimap variants");
            return 0;
        }
    default:
        Console.WriteLine("unknown command"); return 1;
}
