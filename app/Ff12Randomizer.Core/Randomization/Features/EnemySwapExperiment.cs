using System.Buffers.Binary;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Experiment (CLI only, toggle "exp_wolfsewer"): the rats of the first sewer map (grm_g01) get the body of the wolf from
/// the first Estersand area (dst_a). Tests whether an enemy's model can come from another area.
/// How spawns work: each enemy on a map is a script actor calling btlAtelSetUnit(n), n being a basic info record of the
/// area's .ard. That record's byte 0 picks an extended info record (model id u32 at 0x00, animation/ability lists), byte 2
/// an AI script of the .ard's section at header 0x14, and u16 0x08 the name. The map's .ebp lists the models it loads in
/// the section at header 0x40: u32 count, then the model ids as u16 pairs.
/// Here: the rat's extended info record is replaced by the wolf's, the rat basic records take the wolf's name and size,
/// and the rat model in the map's load list becomes the wolf's. Stats, drops and AI stay the rat's.
/// </summary>
public sealed class EnemySwapExperiment : IRandomizerFeature
{
    public const string Id = "exp_wolfsewer";
    private const string TargetArea = "ps2data/plan_master/in/plan_map/grm_g/area/grm_g.ard";
    private const string SourceArea = "ps2data/plan_master/in/plan_map/dst_a/area/dst_a.ard";
    private static readonly string[] TargetMaps =
    [
        "ps2data/plan_master/us/plan_map/grm_g/grm_g01/global/grm_g01.ebp",
        "ps2data/plan_master/in/plan_map/grm_g/grm_g01/global/grm_g01.ebp",
    ];
    private const int TargetExtended = 0, SourceBasic = 2;

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    public void Apply(RandomizerContext context)
    {
        var source = new AreaData(SourceArea, context.Original.Extract(context.Original.Find(SourceArea)!));
        var target = new AreaData(TargetArea, context.Edit(TargetArea));
        var wolfBasic = source.Basic[SourceBasic].Span;
        var wolfExtended = source.Extended[wolfBasic[0]].Span;
        var ratExtended = target.Extended[TargetExtended].Span;
        uint ratModel = BinaryPrimitives.ReadUInt32LittleEndian(ratExtended);
        uint wolfModel = BinaryPrimitives.ReadUInt32LittleEndian(wolfExtended);

        wolfExtended.CopyTo(ratExtended);
        for (int i = 0; i < target.Basic.Count; i++)
        {
            var basic = target.Basic[i].Span;
            if (basic[0] != TargetExtended) continue;
            wolfBasic.Slice(0x08, 8).CopyTo(basic[0x08..]); // name id, then size x/y/z
        }

        foreach (string map in TargetMaps)
        {
            var ebp = context.Edit(map);
            int list = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(0x40));
            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(list));
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var slot = ebp.AsSpan(list + 4 + i * 4, 4);
                if (BinaryPrimitives.ReadUInt32LittleEndian(slot) != ratModel) continue;
                BinaryPrimitives.WriteUInt32LittleEndian(slot, wolfModel);
                found = true;
            }
            if (!found) throw new InvalidDataException($"{map} doesn't load model {ratModel:X8}.");
        }
        context.Spoil("experiments", new { change = "Sewer rats look like Estersand wolves", ratModel = $"{ratModel:X8}", wolfModel = $"{wolfModel:X8}" });
    }
}
