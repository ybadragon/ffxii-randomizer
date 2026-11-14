using System.Diagnostics;

// A map package's "NPS2" record: the world point at its minimap texture's top-left corner and the map's extent in world
// units. Texture pixel = (world - origin) * 4. Read from the .mpk (extracted with the randomizer CLI), cached in nps2.csv.
static class Nps2
{
    static string Cli => Paths.Cli;

    public static (double x0, double z0, double w, double h)? Get(string texdir, string package)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var cache = Path.Combine(texdir, "nps2.csv");
        if (File.Exists(cache))
            foreach (var l in File.ReadAllLines(cache))
            {
                var f = l.Split(',');
                if (f[0] == package) return f.Length < 5 ? null : (double.Parse(f[1], ci), double.Parse(f[2], ci), double.Parse(f[3], ci), double.Parse(f[4], ci));
            }
        string area = package[..3], path = $"ps2data/plan_master/map_ctrl/{area}{package[3..5]}/{package}/bin/{package}.mpk";
        var tmp = Path.Combine(Path.GetTempPath(), package + ".mpk");
        using (var pr = Process.Start(new ProcessStartInfo(Cli, $"extract {path} \"{tmp}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!)
        { pr.StandardOutput.ReadToEnd(); pr.WaitForExit(); }
        (double, double, double, double)? res = null;
        if (File.Exists(tmp))
        {
            var b = File.ReadAllBytes(tmp);
            for (int i = 0; i + 32 <= b.Length; i += 16)
                if (b[i] == 'N' && b[i + 1] == 'P' && b[i + 2] == 'S' && b[i + 3] == '2')
                {
                    res = (BitConverter.ToSingle(b, i + 16), BitConverter.ToSingle(b, i + 20), BitConverter.ToSingle(b, i + 24), BitConverter.ToSingle(b, i + 28));
                    break;
                }
            File.Delete(tmp);
        }
        File.AppendAllLines(cache, [res is { } r ? string.Format(ci, "{0},{1},{2},{3},{4}", package, r.Item1, r.Item2, r.Item3, r.Item4) : package]);
        return res;
    }
}
