using Ff12Randomizer.Core.GameData;

namespace Ff12MapData;

/// <summary>A teleport destination. Each crystal has a hidden key item (KeyItem, 0x8040 and up) that touching it gives;
/// the teleport menu's lines are those items (their names are the region names shown), and the teleport switches on the
/// chosen item. Also: its map, the spot a teleport lands on (X, Z), and the crystal itself (CrystalX, CrystalZ): about 4
/// units from the landing spot, against the landing direction (you land facing away from it; checked on two crystals).</summary>
public sealed record Crystal(int KeyItem, int Map, int Entrance, double X, double Z, double CrystalX, double CrystalZ);

/// <summary>
/// The teleport crystals, read from the teleport controller actor (telepoeffectctrl) that every crystal map's script has.
/// It takes one Teleport Stone (subitem 0x2000), then switches on the chosen key item: case values (PUSHI index into the
/// script's integer table, then op 0x52 label) send each item to a block "PUSHII map; PUSHII entrance; PUSHII 10;
/// CALLPOPA mapjump", and the blocks appear in the same order as their labels.
/// How the menu uses them (system_crystal_actor, decompiled with ff12-script): touching a crystal adds its key item, and
/// appends the item to the saved array g_menu_telepo_dest[31] (savemem 0x0BF2) unless it is there already. The menu
/// asks with 25 lines: line i is g_menu_telepo_dest[i] (shown by the item's name), empty slots hidden, then Cancel.
/// </summary>
public static class Crystals
{
    const string Controller = "telepoeffectctrl";
    const byte PushII = 0x4F, PushI = 0x4E, CallPopA = 0x5D, CaseJump = 0x52;
    const ushort MapJump = 0x008D, TeleportMode = 10;
    static readonly Lazy<IReadOnlyList<Crystal>> All = new(Read);

    /// <summary>Every teleport destination in the game.</summary>
    public static IReadOnlyList<Crystal> List => All.Value;

    /// <summary>Whether the map has a crystal to teleport from (its script has the teleport controller).</summary>
    public static bool HasCrystal(int mapId) => GameFiles.ScriptName(mapId) is { } name && ScriptBytes(name) is { } b
        && new MapScript(b).Actors.Any(a => a.Name == Controller);

    /// <summary>The crystal on a map, from the teleport list (null when nothing teleports there).</summary>
    public static Crystal? On(int mapId) => List.FirstOrDefault(c => c.Map == mapId);

    /// <summary>The crystal whose key item this is (null for other items).</summary>
    public static Crystal? ByKeyItem(int item) => List.FirstOrDefault(c => c.KeyItem == item);

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
                if (ins[i].Op == CaseJump && ins[i - 1].Op == PushI && ins[i - 1].Operand < s.IntConstants.Count)
                    cases.Add((s.IntConstants[ins[i - 1].Operand], ins[i].Operand));
                else if (ins[i].Op == CaseJump && ins[i - 1].Op == PushII)
                    cases.Add(((short)ins[i - 1].Operand, ins[i].Operand));
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
                list.Add(new Crystal(ordered[k].value, map, ent, at.X, at.Z, at.X - Math.Sin(at.Facing) * 4, at.Z - Math.Cos(at.Facing) * 4));
            }
            if (list.Count > 0) return list;
        }
        return [];
    }

    static byte[]? ScriptBytes(string name) => name.Length < 5 ? null : GameFiles.Get($"ps2data/plan_master/us/plan_map/{name[..5]}/{name}/global/{name}.ebp");
}
