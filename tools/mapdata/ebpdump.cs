#:project ../../app/Ff12Randomizer.Core/Ff12Randomizer.Core.csproj
#:property TargetFramework=net10.0-windows
// ebpdump <file.ebp> [actor] : every map jump in a map script with the instructions before it, or all of one actor's code.
using Ff12Randomizer.Core.GameData;
var b = File.ReadAllBytes(args[0]);
var s = new MapScript(b);
foreach (var a in s.Actors)
{
    var ins = s.Instructions(a).ToList();
    bool all = args.Length > 1 && a.Name.Contains(args[1]);
    Console.WriteLine($"== {a.Name} {a.Start:X}-{a.End:X} ({ins.Count} ins)");
    for (int i = 0; i < ins.Count; i++)
    {
        var x = ins[i];
        if (all || (x.Op == 0x5D && x.Operand == 0x8D))
        {
            int from = all ? i : Math.Max(0, i - 8);
            if (!all) Console.WriteLine("  mapjump:");
            for (int k = from; k <= i; k++) Console.WriteLine($"    {ins[k].At:X5} {ins[k].Op:X2} {ins[k].Operand:X4}");
        }
    }
}
