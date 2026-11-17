// Where the test bot writes its pictures and live state. Nothing machine-specific: the repo is found by walking up from
// the running program; set FF12_BOT_OUT to send them elsewhere. Map data comes from tools/MapReader.
static class Paths
{
    public static readonly string Repo = FindRepo();

    /// <summary>Debug pictures and live state; work/bot unless FF12_BOT_OUT is set.</summary>
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
