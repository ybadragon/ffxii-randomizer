#:property TargetFramework=net10.0-windows
// near <file> x z r : float (x,?,z) triples and (x,z) pairs within r of a point
var b = File.ReadAllBytes(args[0]); float X = float.Parse(args[1]), Z = float.Parse(args[2]), R = float.Parse(args[3]);
for (int i = 0; i + 12 <= b.Length; i += 4)
{
    float a = BitConverter.ToSingle(b, i), c = BitConverter.ToSingle(b, i + 4), d = BitConverter.ToSingle(b, i + 8);
    if (Math.Abs(a - X) < R && Math.Abs(d - Z) < R && Math.Abs(c) < 80) Console.WriteLine($"{i:X6} xyz {a:F2} {c:F2} {d:F2}");
    else if (Math.Abs(a - X) < R && Math.Abs(c - Z) < R) Console.WriteLine($"{i:X6} xz  {a:F2} {c:F2}");
}
