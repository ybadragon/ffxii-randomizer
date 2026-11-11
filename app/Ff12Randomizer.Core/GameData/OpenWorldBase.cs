using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Ff12Randomizer.Core.GameData;

/// <summary>
/// The open world's base files: Bartz24's edited map and event scripts and prepared battle_pack.bin sections (MIT, from
/// FFModdingWPF commit f20d432; see third_party/FFModdingWPF/openworld). Entries are named by archive path; the
/// battle_pack pieces are "…/battle_pack.bin.dir/section_NNN.bin" and section 61's text tables
/// "…/battle_pack.bin.dir/section_061.bin.dir/section_NNN.bin".
/// </summary>
public sealed partial class OpenWorldBase
{
    public const string ResourceName = "openworld.zip";

    private OpenWorldBase(Dictionary<string, byte[]> files, Dictionary<int, byte[]> sections, Dictionary<int, byte[]> textTables)
    {
        Files = files;
        Sections = sections;
        TextTables = textTables;
    }

    /// <summary>Whole archive files to replace, by archive path.</summary>
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    /// <summary>battle_pack.bin sections, by section number.</summary>
    public IReadOnlyDictionary<int, byte[]> Sections { get; }

    /// <summary>Section 61 text tables, by table number.</summary>
    public IReadOnlyDictionary<int, byte[]> TextTables { get; }

    public static OpenWorldBase Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The open world files are missing from this build.");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var sections = new Dictionary<int, byte[]>();
        var textTables = new Dictionary<int, byte[]>();
        foreach (var entry in zip.Entries)
        {
            using var data = new MemoryStream();
            using (var entryStream = entry.Open()) entryStream.CopyTo(data);
            var bytes = data.ToArray();

            var match = SectionPath().Match(entry.FullName);
            if (!match.Success) files[entry.FullName] = bytes;
            else if (match.Groups[2].Success) textTables[int.Parse(match.Groups[2].Value)] = bytes;
            else sections[int.Parse(match.Groups[1].Value)] = bytes;
        }
        // Repaired scripts replace the pack's versions (the pack's need Insurgent's Manifesto; see openworld/fixes).
        const string fixPrefix = "openworld-fix/";
        foreach (string resource in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith(fixPrefix)))
        {
            using var fix = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
            using var data = new MemoryStream();
            fix.CopyTo(data);
            files[resource[fixPrefix.Length..]] = data.ToArray();
        }

        return new OpenWorldBase(files, sections, textTables);
    }

    [GeneratedRegex(@"battle_pack\.bin\.dir/section_(\d+)\.bin(?:\.dir/section_(\d+)\.bin)?$")]
    private static partial Regex SectionPath();
}
