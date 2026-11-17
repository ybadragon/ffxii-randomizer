#:property TargetFramework=net10.0-windows
#:project ../../app/Ff12Randomizer.Core/Ff12Randomizer.Core.csproj
using System.Text.RegularExpressions;
using Ff12Randomizer.Core.GameData;
// actor <file.ebp> <actor name> : the actor's code with call names from ff12-script (work/ff12-script)
var calls = new Dictionary<int, string>();
var init = Path.Combine("..", "..", "work", "ff12-script", "ff12-script", "CScriptContainerInitializers.cpp");
foreach (Match m in Regex.Matches(File.ReadAllText(init), @"\{ 0x([0-9a-fA-F]{4}), \{ L""([^""]+)""")) calls.TryAdd(Convert.ToInt32(m.Groups[1].Value, 16), m.Groups[2].Value);
var s = new MapScript(File.ReadAllBytes(args[0]));
var a = s.Actors.First(x => x.Name == args[1]);
string sf = s.StoryFlag.ToString("X");
var names = new Dictionary<byte, string> { [0x48] = "PUSHV", [0x49] = "POPV", [0x4F] = "PUSHII", [0x51] = "JMP", [0x5C] = "JZ?", [0x5D] = "CALL", [0x58] = "CALLX", [0x0A] = "==", [0x0B] = "!=", [0x0C] = ">", [0x0D] = "<", [0x0E] = ">=", [0x0F] = "<=", [0x2D] = "RET?" };
foreach (var x in s.Instructions(a))
{
    string op = names.TryGetValue(x.Op, out var n) ? n : $"op{x.Op:X2}";
    string arg = x.Op < 0x48 ? "" : x.Op is 0x5D or 0x58 && calls.TryGetValue(x.Operand, out var c) ? c : x.Op is 0x48 or 0x49 && x.Operand == s.StoryFlag ? "STORYFLAG" : $"{(short)x.Operand}";
    Console.WriteLine($"  {x.At:X5} {op} {arg}");
}
