using Ff12Randomizer.Core.GameData;

namespace Ff12MapData;

/// <summary>A teleport destination: the crystal's number in the teleport menu's list, its map, and the entrance a teleport
/// lands on, which is beside that map's crystal.</summary>
public sealed record Crystal(int Number, int Map, int Entrance, double X, double Z);

/// <summary>
/// The teleport crystals, read from the teleport controller actor (telepoeffectctrl) that every crystal map's script has.
/// It takes one Teleport Stone (subitem 0x2000), then switches on the chosen crystal number: case values (op 0x4E value,
/// op 0x52 label) send each number to a block "PUSHII map; PUSHII entrance; PUSHII 10; CALLPOPA mapjump", and the blocks
/// appear in the same order as their labels.
/// </summary>
public static class Crystals
{
    const string Controller = "telepoeffectctrl";
    const byte PushII = 0x4F, CallPopA = 0x5D, CaseValue = 0x4E, CaseJump = 0x52;
    const ushort MapJump = 0x008D, TeleportMode = 10;
    static readonly Lazy<IReadOnlyList<Crystal>> All = new(Read);

    /// <summary>Every teleport destination in the game.</summary>
    public static IReadOnlyList<Crystal> List => All.Value;

    /// <summary>Whether the map has a crystal to teleport from (its script has the teleport controller).</summary>
    public static bool HasCrystal(int mapId) => GameFiles.ScriptName(mapId) is { } name && ScriptBytes(name) is { } b
        && new MapScript(b).Actors.Any(a => a.Name == Controller);

    /// <summary>The crystal on a map, from the teleport list (null when nothing teleports there).</summary>
    public static Crystal? On(int mapId) => List.FirstOrDefault(c => c.Map == mapId);

    static IReadOnlyList<Crystal> Read()
    {
        // any crystal map has the full list; the Eastgate's (rbn_a17) is one
        foreach (var name in new[] { "rbn_a17" }.Concat(GameFiles.AllMapIds.Select(GameFiles.ScriptName).OfType<string>()))
        {
            if (ScriptBytes(name) is not { } b) continue;
            var s = new MapScript(b);
            if (s.Actors.FirstOrDefault(a => a.Name == Controller) is not { Name: not null } actor) continue;
            var ins = s.Instructions(actor).ToList();
            var cases = new List<(int value, int label)>();
            var jumps = new List<(int map, int entrance)>();
            for (int i = 1; i < ins.Count; i++)
            {
                if (ins[i].Op == CaseJump && ins[i - 1].Op == CaseValue) cases.Add(((short)ins[i - 1].Operand, ins[i].Operand));
                if (i >= 3 && ins[i].Op == CallPopA && ins[i].Operand == MapJump && ins[i - 1].Op == PushII && ins[i - 1].Operand == TeleportMode
                    && ins[i - 2].Op == PushII && ins[i - 3].Op == PushII)
                    jumps.Add((ins[i - 3].Operand, ins[i - 2].Operand));
            }
            var ordered = cases.OrderBy(c => c.label).ToList();
            var list = new List<Crystal>();
            for (int k = 0; k < Math.Min(ordered.Count, jumps.Count); k++)
            {
                var (map, ent) = jumps[k];
                var at = ScriptInfo.For(GameFiles.ScriptName(map) ?? "") is { } info && ent < info.Entrances.Count ? info.Entrances[ent] : default;
                list.Add(new Crystal(ordered[k].value, map, ent, at.X, at.Z));
            }
            if (list.Count > 0) return list;
        }
        return [];
    }

    static byte[]? ScriptBytes(string name) => name.Length < 5 ? null : GameFiles.Get($"ps2data/plan_master/us/plan_map/{name[..5]}/{name}/global/{name}.ebp");
}
