using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ff12Randomizer.Core.Game;

/// <summary>A located copy of FINAL FANTASY XII THE ZODIAC AGE.</summary>
public sealed partial record GameInstall(string Directory, string? BuildId)
{
    public const string SteamAppId = "595520";

    private static readonly string[] GameProcessNames = ["FFXII_TZA", "FFXII_TZA_GameSetting"];

    public string DataArchivePath => System.IO.Path.Combine(Directory, "FFXII_TZA.vbf");
    public string ExecutableDirectory => System.IO.Path.Combine(Directory, "x64");
    public string ExecutablePath => System.IO.Path.Combine(ExecutableDirectory, "FFXII_TZA.exe");

    /// <summary>Where the loader DLL goes. The game imports DirectInput8Create, so Windows loads this copy.</summary>
    public string LoaderPath => System.IO.Path.Combine(ExecutableDirectory, "dinput8.dll");

    /// <summary>The randomizer's folder inside the game; the loader stays passive when it doesn't exist.</summary>
    public string RandoDirectory => System.IO.Path.Combine(Directory, "ff12rando");
    public string PatchPath => System.IO.Path.Combine(RandoDirectory, "patch.f12p");

    /// <summary>Finds the Steam copy through every Steam library listed in libraryfolders.vdf.</summary>
    public static GameInstall? FindSteamInstall()
    {
        foreach (string library in SteamLibraries())
        {
            string manifest = System.IO.Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;
            string text = File.ReadAllText(manifest);
            string installDir = AcfValue(text, "installdir");
            if (installDir.Length == 0) continue;
            var install = new GameInstall(System.IO.Path.Combine(library, "steamapps", "common", installDir), AcfValue(text, "buildid"));
            if (install.LooksValid) return install;
        }
        return null;
    }

    /// <summary>Accepts a folder the user picked, if it holds the game's data archive and executable.</summary>
    public static GameInstall? FromFolder(string folder)
    {
        var install = new GameInstall(folder, null);
        return install.LooksValid ? install : null;
    }

    private bool LooksValid => File.Exists(DataArchivePath) && File.Exists(ExecutablePath);

    public static bool IsGameRunning()
    {
        foreach (string name in GameProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (running) return true;
        }
        return false;
    }

    /// <summary>Whether this process can add the loader and the randomizer folder to the game.</summary>
    public bool CanWriteGameFolder()
    {
        string probe = System.IO.Path.Combine(ExecutableDirectory, $".ff12rando-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        foreach (var (hive, key, value) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            if (hive.OpenSubKey(key)?.GetValue(value) is string path && path.Length > 0)
                roots.Add(System.IO.Path.GetFullPath(path.Replace('/', '\\')));
        }

        var libraries = new List<string>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libraries.Add(root);
            string vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string AcfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\"{key}\"\\s+\"([^\"]*)\"");
        return match.Success ? match.Groups[1].Value : "";
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();
}
