#:property TargetFramework=net10.0-windows
#:project ../../app/Ff12Randomizer.Core/Ff12Randomizer.Core.csproj
using Ff12Randomizer.Core.GameData;
// blockers <file.ebp> : POS3 entries, every trigger line with its owner actor, and each actor's story checks
var b = File.ReadAllBytes(args[0]);
int I(int o) => BitConverter.ToInt32(b, o); float F(int o) => BitConverter.ToSingle(b, o);
var s = new MapScript(b);
var checks = s.StoryChecks().GroupBy(c => c.Actor).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(c => $"{c.Compare} {c.Value}").Distinct()));
var owner = new Dictionary<int, string>();
foreach (var a in s.Actors)
{
    var ins = s.Instructions(a).ToList();
    for (int i = 1; i < Math.Min(ins.Count, 8); i++) if (ins[i].Op == 0x5D && ins[i].Operand == 0x11E && ins[i - 1].Op == 0x4F) owner[ins[i - 1].Operand] = a.Name;
}
int bs = I(0x10), tl = bs + I(bs + 0x70), nl = I(tl);
Console.WriteLine("trigger lines:");
for (int l = 0; l < nl; l++)
{
    int o = bs + I(tl + 4 + l * 4), n = I(o);
    for (int k = 0; k < n; k++)
    {
        int sg = o + 0x10 + k * 0x20; int id = (I(sg + 0x1C) >> 8) & 0xFF;
        string who = owner.TryGetValue(id, out var w) ? w : "?";
        Console.WriteLine($"  list {l} id {id,3} ({F(sg):F1},{F(sg + 8):F1})-({F(sg + 16):F1},{F(sg + 24):F1}) flags {I(sg + 0x1C):X8} owner {who} {(checks.TryGetValue(who, out var c) ? "[" + c + "]" : "")}");
    }
}
int p = -1; for (int i = 0; i + 8 < b.Length; i += 4) if (System.Text.Encoding.ASCII.GetString(b, i, 8) == "FF12POS3") { p = i; break; }
Console.WriteLine("POS3:");
for (int i = 0; i < I(p + 12); i++)
{
    int e = p + 0x10 + i * 0x30;
    if (F(e + 16) != 0 || F(e + 24) != 0) Console.WriteLine($"  {i,3}: ({F(e + 16):F1}, {F(e + 20):F1}, {F(e + 24):F1}) rot {F(e + 28):F2} size {F(e + 4)}x{F(e + 8)}  raw0 {I(e):X8}");
}
Console.WriteLine("actors with story checks:");
foreach (var (a, c) in checks) Console.WriteLine($"  {a}: {c}");
