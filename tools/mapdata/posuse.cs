#:property TargetFramework=net10.0-windows
#:project ../../app/Ff12Randomizer.Core/Ff12Randomizer.Core.csproj
using Ff12Randomizer.Core.GameData;
// posuse <file.ebp> <n> : every "PUSHII n" followed within 4 instructions by a CALLPOPA, per actor (which calls take n)
var s = new MapScript(File.ReadAllBytes(args[0])); int n = int.Parse(args[1]);
foreach (var a in s.Actors)
{
    var ins = s.Instructions(a).ToList();
    for (int i = 0; i < ins.Count; i++)
        if (ins[i].Op == 0x4F && ins[i].Operand == n)
            for (int k = i + 1; k < Math.Min(ins.Count, i + 5); k++)
                if (ins[k].Op is 0x5D or 0x58) { Console.WriteLine($"{a.Name,-24} {ins[i].At:X5}: " + string.Join(" ", ins.Skip(Math.Max(0, i - 3)).Take(k - i + 4).Select(x => $"{x.Op:X2}:{x.Operand:X}"))); break; }
}
