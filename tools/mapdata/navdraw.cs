#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
using System.Drawing;
// navdraw <mpk> <tex.png> <x0> <z0> <out.png> : draws the walk mesh (triangles by group, special edges) over the texture
var b = File.ReadAllBytes(args[0]);
int o = -1;
for (int i = 0; i + 0x30 < b.Length; i += 16)
    if (BitConverter.ToInt32(b, i) == 1 && BitConverter.ToInt32(b, i + 12) == 0x30 && BitConverter.ToInt32(b, i + 16) > 0x30) { o = i; break; }
int nv = BitConverter.ToUInt16(b, o + 4), nt = BitConverter.ToUInt16(b, o + 6), ne = BitConverter.ToUInt16(b, o + 8), ng = BitConverter.ToUInt16(b, o + 10);
int[] off = Enumerable.Range(0, 8).Select(k => o + BitConverter.ToInt32(b, o + 12 + k * 4)).ToArray();
Console.WriteLine($"mesh at {o:X}: {nv} verts, {nt} tris, {ne} edges, {ng} groups; sections " + string.Join(",", off.Select(x => x.ToString("X"))));
var V = Enumerable.Range(0, nv).Select(i => (x: BitConverter.ToSingle(b, off[0] + i * 8), z: BitConverter.ToSingle(b, off[0] + i * 8 + 4))).ToArray();
double x0 = double.Parse(args[2]), z0 = double.Parse(args[3]);
var img = new Bitmap(args[1]); var bmp = new Bitmap(img.Width * 2, img.Height * 2);
using var g = Graphics.FromImage(bmp);
g.DrawImage(img, 0, 0, bmp.Width, bmp.Height);
PointF P(int v) => new((float)((V[v].x - x0) * 8), (float)((V[v].z - z0) * 8));
var groups = new Dictionary<int, int>();
for (int t = 0; t < nt; t++)
{
    int e = off[1] + t * 8; int a = BitConverter.ToUInt16(b, e), c = BitConverter.ToUInt16(b, e + 2), d = BitConverter.ToUInt16(b, e + 4), grp = BitConverter.ToUInt16(b, e + 6);
    groups[grp] = groups.GetValueOrDefault(grp) + 1;
    var col = Color.FromArgb(90, (grp * 97) % 256, (grp * 57 + 80) % 256, (grp * 151 + 30) % 256);
    g.FillPolygon(new SolidBrush(col), new[] { P(a), P(c), P(d) });
    g.DrawPolygon(new Pen(Color.FromArgb(120, 255, 255, 255)), new[] { P(a), P(c), P(d) });
}
Console.WriteLine("tri groups: " + string.Join(" ", groups.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")));
var attrs = new Dictionary<string, int>();
for (int i = 0; i < ne; i++)
{
    int e = off[2] + i * 8; int a = BitConverter.ToUInt16(b, e), c = BitConverter.ToUInt16(b, e + 2), f1 = BitConverter.ToUInt16(b, e + 4), f2 = BitConverter.ToUInt16(b, e + 6);
    attrs[$"{f1:X}/{f2:X}"] = attrs.GetValueOrDefault($"{f1:X}/{f2:X}") + 1;
    if (a < nv && c < nv) g.DrawLine(new Pen(f2 == 0x2F ? Color.Yellow : Color.Lime, 3), P(a), P(c));
    if (i < 4 || f2 != 0x2F) Console.WriteLine($"  edge {i}: {a} {c} {f1:X} {f2:X}  ({V[Math.Min(a, nv - 1)].x:F1},{V[Math.Min(a, nv - 1)].z:F1})-({V[Math.Min(c, nv - 1)].x:F1},{V[Math.Min(c, nv - 1)].z:F1})");
}
Console.WriteLine("edge attrs: " + string.Join(" ", attrs.Select(k => $"{k.Key}:{k.Value}")));
bmp.Save(args[4]);
