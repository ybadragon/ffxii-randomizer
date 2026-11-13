using System.Globalization;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Our own open world, stage by stage (CLI toggle "exp_openroads" until it is confirmed in game): roads the story keeps
/// closed are open from the start, while the story itself still advances in order.
/// <para>The game closes a road with a comparison in the map's script: a guard, door or exit that only works once the
/// story flag has reached some value, or a state variable set to "closed". data/opengates.csv lists each one: the map
/// script, the actor, the variable (address:type in the script's variable table; 0000:3000 is the story flag), the
/// comparison or assignment and its constant, and the constant that makes it always open. Each row must match exactly
/// the stated number of places, or the run fails; nothing but those constants changes, so every file keeps its length.</para>
/// <para>Teleport crystals: the boot script pre_z0000 turns them off (setsaveramtelepostatus(0), call 0x0305) for story
/// values below 105 and again during the story's captive stretches. Only the first is switched on; teleporting out of the
/// captive stretches would break the story.</para>
/// </summary>
public sealed class OpenRoadsFeature : IRandomizerFeature
{
    public const string Id = "exp_openroads";
    private const ushort TeleportStatusCall = 0x0305;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id) && !settings.IsOn(OpenWorldFeature.Id);

    private sealed record Gate(int Stage, string Map, string Actor, ushort Address, ushort Type, string Kind, string Compare,
        short Value, short NewValue, int Count, string Note);

    private static List<Gate> Gates() => Csv.ReadEmbedded("opengates.csv").Skip(1).Where(r => r.Count >= 10).Select(r =>
    {
        var variable = r[3].Split(':');
        return new Gate(int.Parse(r[0]), r[1], r[2], Convert.ToUInt16(variable[0], 16), Convert.ToUInt16(variable[1], 16), r[4], r[5],
            short.Parse(r[6], CultureInfo.InvariantCulture), short.Parse(r[7], CultureInfo.InvariantCulture), int.Parse(r[8]), r[9]);
    }).ToList();

    public void Apply(RandomizerContext context)
    {
        var paths = context.Original.Entries.Where(e => e.Name.StartsWith("ps2data/plan_master/us/") && e.Name.EndsWith(".ebp"))
            .GroupBy(e => Path.GetFileNameWithoutExtension(e.Name)).ToDictionary(g => g.Key, g => g.First().Name);

        foreach (var map in Gates().GroupBy(g => g.Map))
        {
            if (!paths.TryGetValue(map.Key, out string? path)) throw new InvalidDataException($"Open roads: no script named {map.Key}.");
            var script = new MapScript(context.Edit(path));
            foreach (var gate in map)
            {
                ushort variable = script.Variable(gate.Address, gate.Type)
                    ?? throw new InvalidDataException($"Open roads: {gate.Map} has no variable {gate.Address:X4}:{gate.Type:X4}.");
                var sites = gate.Kind == "write"
                    ? script.Writes(variable).Where(w => w.Value == gate.Value && (gate.Actor.Length == 0 || w.Actor == gate.Actor)).Select(w => w.At).ToList()
                    : script.Checks(variable).Where(c => c.Value == gate.Value && c.Compare == gate.Compare && c.Actor == gate.Actor).Select(c => c.At).ToList();
                if (sites.Count != gate.Count)
                    throw new InvalidDataException($"Open roads: {gate.Map} {gate.Actor} \"{gate.Compare} {gate.Value}\" matched {sites.Count} places, expected {gate.Count}.");
                foreach (int at in sites) script.SetConstant(at, gate.NewValue);
                context.Spoil("openRoads", new { place = gate.Note, map = gate.Map });
            }
        }

        TeleportFromTheStart(context, paths["pre_z0000"]);
    }

    /// <summary>The first setsaveramtelepostatus(0) of the boot script (story values below 105) becomes (1).</summary>
    private static void TeleportFromTheStart(RandomizerContext context, string path)
    {
        var script = new MapScript(context.Edit(path));
        var calls = new List<(int ArgumentAt, ushort Argument)>();
        foreach (var actor in script.Actors)
        {
            var code = script.Instructions(actor).ToList();
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i] is { Op: MapScript.CallPopA, Operand: TeleportStatusCall } && code[i - 1].Op == MapScript.PushII)
                    calls.Add((code[i - 1].At, code[i - 1].Operand));
            }
        }
        if (calls.Count < 2 || calls[0].Argument != 0 || calls[1].Argument != 1)
            throw new InvalidDataException("Open roads: the teleport crystal switch in pre_z0000 isn't where expected.");
        script.SetConstant(calls[0].ArgumentAt, 1);
        context.Spoil("openRoads", new { place = "Teleport crystals work from the start (still off during the story's captive stretches)", map = "pre_z0000" });
    }
}
