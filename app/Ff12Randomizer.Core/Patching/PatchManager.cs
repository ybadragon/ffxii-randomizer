using System.Reflection;
using System.Security.Cryptography;
using Ff12Randomizer.Core.Game;
using Ff12Randomizer.Core.Vbf;

namespace Ff12Randomizer.Core.Patching;

public enum GameDataStatus
{
    /// <summary>No randomizer files in the game: it plays exactly as Steam installed it.</summary>
    Original,
    /// <summary>The loader and a seed patch are installed.</summary>
    Randomized,
    /// <summary>Some other x64\dinput8.dll is installed (another mod loader); the randomizer leaves it alone.</summary>
    OtherLoader,
}

public sealed class GameRunningException() : Exception("FINAL FANTASY XII is running. Close the game first.");

public sealed class OtherLoaderException(string path)
    : Exception($"Another mod's dinput8.dll is installed ({path}). The randomizer won't replace it. Remove that mod first.");

public sealed class PatchFailedException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Installs and removes a randomization. The game archive itself is never written: a run is the loader DLL plus
/// ff12rando\patch.f12p, and removing both gives back the original game.
/// </summary>
public sealed class PatchManager(GameInstall game, Func<bool>? isGameRunning = null)
{
    /// <summary>Text the loader writes to its log; any DLL containing it is one of ours (maybe an older build).</summary>
    private static readonly byte[] LoaderMarker = "ff12rando loader starting"u8.ToArray();

    private readonly Func<bool> _isGameRunning = isGameRunning ?? GameInstall.IsGameRunning;

    public GameInstall Game { get; } = game;

    /// <summary>The loader DLL shipped inside this app.</summary>
    public static byte[] LoaderBytes { get; } = LoadEmbeddedLoader();

    public GameDataStatus GetStatus()
    {
        if (File.Exists(Game.LoaderPath) && !IsOurLoader(Game.LoaderPath)) return GameDataStatus.OtherLoader;
        return File.Exists(Game.LoaderPath) && File.Exists(Game.PatchPath) ? GameDataStatus.Randomized : GameDataStatus.Original;
    }

    /// <summary>Identifies the installed patch; history entries use it to mark the run that is in the game now.</summary>
    public string? CurrentFingerprint() => File.Exists(Game.PatchPath) ? Fingerprint(Game.PatchPath) : null;

    /// <summary>The game data, which is always the original: the randomizer never writes to the archive.</summary>
    public VbfArchive OpenOriginal()
    {
        var archive = new VbfArchive(Game.DataArchivePath);
        if (!archive.HeaderChecksumValid())
            throw new InvalidDataException("FFXII_TZA.vbf looks damaged or was changed by another tool. Verify the game files in Steam, then try again.");
        return archive;
    }

    /// <summary>Writes <paramref name="files"/> as the new patch, checks every file reads back exactly, and installs the loader.</summary>
    public string Apply(VbfArchive original, IReadOnlyDictionary<string, byte[]> files, IReadOnlyCollection<string>? loaderFeatures = null)
    {
        if (_isGameRunning()) throw new GameRunningException();
        if (GetStatus() == GameDataStatus.OtherLoader) throw new OtherLoaderException(Game.LoaderPath);

        Directory.CreateDirectory(Game.RandoDirectory);
        string staging = Path.Combine(Game.RandoDirectory, "patch.new.f12p");
        try
        {
            VbfPatchBuilder.Build(original, files, staging);
            Verify(original, files, staging);
            File.Move(staging, Game.PatchPath, overwrite: true);
            InstallLoader();
            WriteLoaderFeatures(loaderFeatures ?? []);
        }
        catch (Exception e) when (e is not GameRunningException)
        {
            TryDelete(staging);
            throw new PatchFailedException("Writing the randomized game didn't finish. Your game wasn't changed.", e);
        }

        return Fingerprint(Game.PatchPath);
    }

    /// <summary>Removes the loader and the randomizer folder. Returns false when there was nothing to remove.</summary>
    public bool Restore()
    {
        var status = GetStatus();
        bool hasFolder = Directory.Exists(Game.RandoDirectory);
        bool hasLoader = status != GameDataStatus.OtherLoader && File.Exists(Game.LoaderPath);
        if (!hasFolder && !hasLoader) return false;
        if (_isGameRunning()) throw new GameRunningException();
        if (hasLoader) File.Delete(Game.LoaderPath);
        if (hasFolder) Directory.Delete(Game.RandoDirectory, recursive: true);
        return true;
    }

    /// <summary>One empty "&lt;name&gt;.on" file per loader feature; stale ones from an earlier run are removed.</summary>
    private void WriteLoaderFeatures(IReadOnlyCollection<string> features)
    {
        foreach (string stale in Directory.GetFiles(Game.RandoDirectory, "*.on"))
        {
            if (!features.Contains(Path.GetFileNameWithoutExtension(stale))) File.Delete(stale);
        }
        foreach (string feature in features) File.WriteAllBytes(Path.Combine(Game.RandoDirectory, feature + ".on"), []);
    }

    private void InstallLoader()
    {
        if (File.Exists(Game.LoaderPath) && File.ReadAllBytes(Game.LoaderPath).AsSpan().SequenceEqual(LoaderBytes)) return;
        string temp = Game.LoaderPath + ".tmp";
        File.WriteAllBytes(temp, LoaderBytes);
        File.Move(temp, Game.LoaderPath, overwrite: true);
    }

    /// <summary>Reads every changed file back through the same logic the loader uses, plus the header checksum.</summary>
    public static void Verify(VbfArchive original, IReadOnlyDictionary<string, byte[]> files, string patchPath)
    {
        var patched = new VbfArchive(original.Path, () => new VirtualVbfStream(original.Path, patchPath));
        if (!patched.HeaderChecksumValid()) throw new InvalidDataException("Header checksum mismatch in the new patch.");
        foreach (var (name, expected) in files)
        {
            var entry = patched.Find(name) ?? throw new InvalidDataException($"{name} is missing from the new patch.");
            if (!patched.Extract(entry).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException($"{name} didn't read back correctly from the new patch.");
        }
    }

    /// <summary>The fingerprint the patch for <paramref name="files"/> would have, built in a temp file; the game isn't touched.</summary>
    public static string FingerprintOf(VbfArchive original, IReadOnlyDictionary<string, byte[]> files)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"ff12rando-check-{Guid.NewGuid():N}.f12p");
        try
        {
            VbfPatchBuilder.Build(original, files, temp);
            return Fingerprint(temp);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static bool IsOurLoader(string path) => File.ReadAllBytes(path).AsSpan().IndexOf(LoaderMarker) >= 0;

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))[..16];
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static byte[] LoadEmbeddedLoader()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("dinput8.dll")
            ?? throw new InvalidOperationException("The loader DLL is missing from this build.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
