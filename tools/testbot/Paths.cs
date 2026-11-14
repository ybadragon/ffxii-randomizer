// Where the test bot finds the repo's tools and data, and where it writes its pictures. Nothing machine-specific:
// the repo is found by walking up from the running program; set FF12_BOT_OUT to send debug pictures elsewhere.
static class Paths
{
    public static readonly string Repo = FindRepo();
    /// <summary>The randomizer CLI (build app/Ff12Randomizer.Cli first): extracts game files.</summary>
    public static string Cli => Path.Combine(Repo, "app", "Ff12Randomizer.Cli", "bin", "Debug", "net10.0-windows", "Ff12Randomizer.Cli.exe");
    /// <summary>Map id to script name.</summary>
    public static string MapAreas => Path.Combine(Repo, "app", "Ff12Randomizer.Core", "Data", "mapAreas.csv");
    /// <summary>Every map's exits, read from the map scripts by tools/mapdata/exits.cs.</summary>
    public static string Exits => Path.Combine(Repo, "tools", "mapdata", "exits.csv");
    /// <summary>Map id, script name and the game's place name (tools/mapdata/exits.cs names).</summary>
    public static string MapNames => Path.Combine(Repo, "tools", "mapdata", "mapnames.csv");
    /// <summary>Extracted game files (not in git): menu map files and, in tex/, each map package's minimap picture.</summary>
    public static string MapRes => Path.Combine(Repo, "work", "mapres");
    public static string Tex => Path.Combine(MapRes, "tex");
    /// <summary>Debug pictures (route plans, captured minimaps); work/bot unless FF12_BOT_OUT is set.</summary>
    public static string Out(string sub)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("FF12_BOT_OUT") is { Length: > 0 } o ? o : Path.Combine(Repo, "work", "bot"), sub);
        Directory.CreateDirectory(d);
        return d;
    }

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "app", "Ff12Randomizer.Core"))) return d.FullName;
        throw new InvalidOperationException("run the test bot from inside the randomizer repo");
    }
}
