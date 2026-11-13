using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Developer test switch "exp_weak=&lt;text&gt;": every enemy in the area files whose archive path contains the text has
/// 1 HP, so a test can beat a boss quickly (for example "mfr_b" for Rafflesia). Never part of a normal seed.
/// </summary>
public sealed class WeakEnemiesExperiment : IRandomizerFeature
{
    public const string Id = "exp_weak";

    public bool IsEnabled(RandomizerSettings settings) => settings.Choices.ContainsKey(Id);

    public void Apply(RandomizerContext context)
    {
        string text = context.Settings.Choices[Id];
        foreach (var entry in context.Original.Entries.Where(e => AreaData.IsAreaFile(e.Name) && e.Name.Contains(text)))
        {
            var area = new AreaData(entry.Name, context.Edit(entry.Name));
            foreach (var table in new[] { area.DefaultStats, area.LevelStats })
                for (int i = 0; i < table.Count; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(table[i].Span[0x20..], 1);
            context.Spoil("weak", new { file = entry.Name });
        }
    }
}
