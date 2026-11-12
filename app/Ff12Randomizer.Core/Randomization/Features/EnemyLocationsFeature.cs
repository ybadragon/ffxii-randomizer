using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// Regular enemies trade places: the kinds of enemy that roam each area are shuffled between all areas.
/// <para>How spawns work (script layout per ffgriever's ff12-script, BSD-2-Clause): each enemy on a map is a script actor
/// that calls btlAtelSetUnit(n) (call 0x3001), n being a basic info record of the area's .ard. Byte 0 of that record picks an
/// extended info record: the body (model id u32 at 0x00, animations, abilities, flying). The u16 at 0x08 is the name and
/// 0x0A-0x0F the size. Each map's .ebp lists the models it loads in the section at header 0x40 (u32 count, u32 ids).</para>
/// <para>Which enemies are regular comes from the developers' own actor names in the map scripts: regular spawns are
/// actors named monster_NN; marks are NM_..., rare game unique_..., bosses BOSS_..., trial mode trial... A body is only
/// moved when every actor that spawns it, on every map of its area, is a monster_NN actor, and no other enemy of the area
/// shares its model.</para>
/// <para>What moves is the body, name and size. Stats, level, drops and battle AI stay with the place, so an area keeps
/// its difficulty: Estersand wolves in the first sewer are as weak as the rats they replace.</para>
/// </summary>
public sealed partial class EnemyLocationsFeature : IRandomizerFeature
{
    public const string Id = "enemyspots";
    private const ushort SetUnitCall = 0x3001;
    private const byte PushII = 0x4F, CallPopA = 0x5D;

    [GeneratedRegex(@"^monster_\d+$")]
    private static partial Regex RegularActor();

    public bool IsEnabled(RandomizerSettings settings) => settings.IsOn(Id);

    /// <summary>The enemies of one model in one area that may move: the extended records and the basic records using them.</summary>
    private sealed record Slot(AreaData Area, uint Model, List<int> Extended, List<int> Basics);

    /// <summary>A body to hand out: one extended record with the name and size that go with it.</summary>
    private sealed record Body(uint Model, byte[] Extended, byte[] NameAndSize, ushort NameId, byte[] Rewards, byte[] DefaultStats, byte[] LevelStats);

    public const string OwnId = "enemyown";
    // Basic record: drops, steals, poaches and their chances; and the indexes of its two stats records.
    private const int RewardsStart = 0x28, RewardsEnd = 0x50, DefaultStatsIndex = 0x22, LevelStatsIndex = 0x24;

    public void Apply(RandomizerContext context)
    {
        var archive = context.Original;
        // A file as it stands now: another option (the open world) may already have replaced it.
        byte[] Current(string path) => context.ChangedFiles.GetValueOrDefault(path) ?? archive.Extract(archive.Find(path)!);
        var mapsByArea = archive.Entries
            .Where(e => e.Name.StartsWith("ps2data/plan_master/us/plan_map/") && e.Name.EndsWith(".ebp") && e.Name.Contains("/global/"))
            .GroupBy(e => e.Name.Split('/')[4])
            .ToDictionary(g => g.Key, g => g.Select(e => e.Name).ToList());

        var slots = new List<Slot>();
        foreach (var entry in archive.Entries.Where(e => AreaData.IsAreaFile(e.Name)).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var area = new AreaData(entry.Name, context.Edit(entry.Name));
            if (!mapsByArea.TryGetValue(area.AreaId, out var maps)) continue;

            // Which units are spawned, and whether only by regular actors.
            var regular = new Dictionary<int, bool>();
            foreach (string map in maps)
            {
                foreach (var (actor, unit) in Spawns(Current(map)))
                    regular[unit] = regular.GetValueOrDefault(unit, true) && RegularActor().IsMatch(actor);
            }

            var basicsOf = Enumerable.Range(0, area.Basic.Count).GroupBy(b => (int)area.Basic[b].Span[0])
                .Where(g => g.Key < area.Extended.Count).ToDictionary(g => g.Key, g => g.ToList());
            uint ModelOf(int ext) => BinaryPrimitives.ReadUInt32LittleEndian(area.Extended[ext].Span);
            bool Movable(int ext) => basicsOf.TryGetValue(ext, out var basics) && basics.All(b => regular.GetValueOrDefault(b));
            foreach (var group in Enumerable.Range(0, area.Extended.Count).GroupBy(ModelOf))
            {
                // Every enemy of this model must be regular, or the map would still need the old model.
                if (!group.All(Movable)) continue;
                slots.Add(new Slot(area, group.Key, group.ToList(), group.SelectMany(e => basicsOf[e]).ToList()));
            }
        }

        var bodies = slots.Select(s =>
        {
            var basic = s.Area.Basic[s.Basics[0]].Span;
            return new Body(s.Model, s.Area.Extended[basic[0]].ToArray(), basic.Slice(0x08, 8).ToArray(),
                BinaryPrimitives.ReadUInt16LittleEndian(basic[0x08..]), basic[RewardsStart..RewardsEnd].ToArray(),
                s.Area.DefaultStats[basic[DefaultStatsIndex]].ToArray(), s.Area.LevelStats[basic[LevelStatsIndex]].ToArray());
        }).ToList();
        var dealt = bodies.ToList();
        context.RngFor(Id).Shuffle(dealt);

        var names = new MapNames(archive);
        var pack = new BattlePack(archive.Extract(archive.Find(BattlePack.UsPath)!));
        string NameOf(ushort nameId) => pack.Text(BattlePack.NamesTable, nameId & 0xFFF) ?? $"Enemy {nameId:X4}";

        bool own = context.Settings.IsOn(OwnId);
        var modelChanges = new Dictionary<string, Dictionary<uint, uint>>();
        for (int i = 0; i < slots.Count; i++)
        {
            var (slot, body) = (slots[i], dealt[i]);
            foreach (int ext in slot.Extended) body.Extended.CopyTo(slot.Area.Extended[ext].Span);
            foreach (int basic in slot.Basics) body.NameAndSize.CopyTo(slot.Area.Basic[basic].Span[0x08..]);
            if (own) BringOwn(slot, body);
            if (!modelChanges.TryGetValue(slot.Area.AreaId, out var changes)) modelChanges[slot.Area.AreaId] = changes = [];
            changes[slot.Model] = body.Model;
            if (bodies[i].NameId != body.NameId)
            {
                context.Spoil("enemyLocations", new
                {
                    area = names.RegionOf(slot.Area.AreaId) ?? names.NameOf(slot.Area.AreaId + "01") ?? slot.Area.AreaId,
                    areaId = slot.Area.AreaId,
                    was = NameOf(bodies[i].NameId),
                    now = NameOf(body.NameId),
                });
            }
        }

        // Each map of a changed area loads the new models instead of the old ones.
        foreach (var (areaId, changes) in modelChanges)
        {
            foreach (string usMap in mapsByArea[areaId])
            {
                foreach (string map in new[] { usMap, usMap.Replace("/us/", "/in/") })
                {
                    if (archive.Find(map) is null) continue;
                    var now = Current(map);
                    if (LoadList(now) is not var (at, count) || !Enumerable.Range(0, count).Any(i => changes.ContainsKey(ModelAt(now, at, i)))) continue;
                    var ebp = context.Edit(map);
                    var models = Enumerable.Range(0, count).Select(i => ModelAt(ebp, at, i))
                        .Select(m => changes.GetValueOrDefault(m, m)).Distinct().ToList();
                    BinaryPrimitives.WriteUInt32LittleEndian(ebp.AsSpan(at), (uint)models.Count);
                    for (int i = 0; i < count; i++)
                        BinaryPrimitives.WriteUInt32LittleEndian(ebp.AsSpan(at + 4 + i * 4), i < models.Count ? models[i] : 0);
                }
            }
        }
    }

    /// <summary>
    /// "Keep their own stats and drops": the enemy brings its rewards and both stats records. A stats record is only
    /// overwritten when no enemy outside this slot uses it (a mark sharing it keeps its stats). Levels still come from
    /// the spawn scripts of the place.
    /// </summary>
    private static void BringOwn(Slot slot, Body body)
    {
        var area = slot.Area;
        bool Private(int offset, byte index) =>
            Enumerable.Range(0, area.Basic.Count).Where(b => area.Basic[b].Span[offset] == index).All(slot.Basics.Contains);
        foreach (int b in slot.Basics)
        {
            var basic = area.Basic[b].Span;
            body.Rewards.CopyTo(basic[RewardsStart..]);
            if (basic[DefaultStatsIndex] < area.DefaultStats.Count && Private(DefaultStatsIndex, basic[DefaultStatsIndex]))
                body.DefaultStats.CopyTo(area.DefaultStats[basic[DefaultStatsIndex]].Span);
            if (basic[LevelStatsIndex] < area.LevelStats.Count && Private(LevelStatsIndex, basic[LevelStatsIndex]))
                body.LevelStats.CopyTo(area.LevelStats[basic[LevelStatsIndex]].Span);
        }
    }

    private static uint ModelAt(byte[] ebp, int list, int index) => BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(list + 4 + index * 4));

    /// <summary>The model load list of a map file: its offset and entry count, or null when the map has none.</summary>
    private static (int At, int Count)? LoadList(byte[] ebp)
    {
        if (ebp.Length < 0x44) return null;
        int at = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(0x40));
        if (at <= 0 || at + 4 > ebp.Length) return null;
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(at));
        return count is > 0 and < 256 && at + 4 + count * 4 <= ebp.Length ? (at, count) : null;
    }

    /// <summary>
    /// Every "btlAtelSetUnit(n)" in a map script with the name of the actor it is in. The script starts at the u32 at 0x10
    /// of the .ebp; its header has the actor table at 0x18 (u32 count, then 0x30-byte entries: name offset at 0x00, code
    /// start at 0x08) and the name text at 0x4C.
    /// </summary>
    public static IEnumerable<(string Actor, int Unit)> Spawns(byte[] ebp)
    {
        int script = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(0x10));
        if (script <= 0 || script + 0x50 > ebp.Length) yield break;
        int table = script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(script + 0x18));
        int nameText = script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(script + 0x4C));
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(table));
        var actors = new List<(string Name, int Start)>();
        for (int i = 0; i < count; i++)
        {
            int entry = table + 4 + i * 0x30;
            int name = nameText + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(entry));
            int end = Array.IndexOf(ebp, (byte)0, name);
            actors.Add((Encoding.Latin1.GetString(ebp, name, end - name), script + (int)BinaryPrimitives.ReadUInt32LittleEndian(ebp.AsSpan(entry + 8))));
        }
        var ordered = actors.OrderBy(a => a.Start).ToList();
        for (int a = 0; a < ordered.Count; a++)
        {
            int end = a + 1 < ordered.Count ? ordered[a + 1].Start : Math.Min(ebp.Length, table);
            for (int p = ordered[a].Start; p + 6 <= end; p++)
            {
                if (ebp[p] == PushII && ebp[p + 3] == CallPopA && BinaryPrimitives.ReadUInt16LittleEndian(ebp.AsSpan(p + 4)) == SetUnitCall)
                    yield return (ordered[a].Name, BinaryPrimitives.ReadUInt16LittleEndian(ebp.AsSpan(p + 1)));
            }
        }
    }
}
