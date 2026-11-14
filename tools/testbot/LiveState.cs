using System.Text.Json;

// What the walking commands are doing right now, for the live viewer (`memtool viz`): tnav writes the route it is
// following (every time it plans), trip writes the chain of maps and the exit it is heading for. Small JSON files in
// work/bot/live, replaced atomically so the viewer never reads half a file.
static class LiveState
{
    public sealed record RouteState(int Map, double[][] Route, double[] Goal, double[][] Blocks, string Status, long Time);
    public sealed record TripState(string[] Chain, int[] ChainIds, int Current, int NextMap, double[]? Exit, string Goal, string Status, long Time);

    static string Dir => Paths.Out("live");
    public static string RouteFile => Path.Combine(Dir, "route.json");
    public static string TripFile => Path.Combine(Dir, "trip.json");

    public static void Route(int map, IEnumerable<(double x, double z)> route, (double x, double z) goal, IEnumerable<(double x, double z)> blocks, string status) =>
        Write(RouteFile, new RouteState(map, route.Select(p => new[] { p.x, p.z }).ToArray(), [goal.x, goal.z],
            blocks.Select(p => new[] { p.x, p.z }).ToArray(), status, DateTime.UtcNow.Ticks));

    public static void Trip(List<(int id, string name)> chain, int current, int nextMap, (double x, double z)? exit, string goal, string status) =>
        Write(TripFile, new TripState(chain.Select(c => c.name).ToArray(), chain.Select(c => c.id).ToArray(), current, nextMap,
            exit is { } e ? [e.x, e.z] : null, goal, status, DateTime.UtcNow.Ticks));

    public static T? Load<T>(string file) where T : class
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : null; }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    static void Write<T>(string file, T value)
    {
        try
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value));
            File.Move(tmp, file, true);
        }
        catch (IOException) { } // the viewer had it open for a moment; the next write catches up
        catch (UnauthorizedAccessException) { }
    }
}
