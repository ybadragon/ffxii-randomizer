namespace Ff12Randomizer.Core.Randomization;

public sealed record Choice(string Id, string Label);

public sealed record SubOption(string Id, string Label, string Description, string? Warning = null, bool Implemented = true);

/// <summary>
/// One feature box in the options window. A box with <see cref="HasToggle"/> is switched on by its own id;
/// a box with <see cref="Choices"/> stores the picked choice id under its own id (the first choice is the default).
/// </summary>
public sealed record OptionGroup(
    string Id,
    string Title,
    string Description,
    bool HasToggle,
    IReadOnlyList<Choice> Choices,
    IReadOnlyList<SubOption> SubOptions,
    string? Warning = null,
    string? Badge = null,
    bool Implemented = false)
{
    public OptionDropdown? Dropdown { get; init; }
}

/// <summary>
/// A second setting on a feature box, picked from a list: stored under its own id in <see cref="RandomizerSettings.Choices"/>,
/// shown only while the box's choice is one of <see cref="ShownFor"/>. The first choice is the default. Picking a choice
/// listed in <see cref="Warnings"/> shows that warning.
/// </summary>
public sealed record OptionDropdown(
    string Id,
    string Label,
    IReadOnlyList<Choice> Choices,
    IReadOnlySet<string> ShownFor,
    string? Hint = null,
    IReadOnlyDictionary<string, string>? Warnings = null)
{
    /// <summary>For a dropdown that also takes values not in <see cref="Choices"/>: the label of such a value, or null.</summary>
    public Func<string, string?>? LabelOf { get; init; }

    /// <summary>The label of a value: its choice's, or what <see cref="LabelOf"/> makes of it; null when it isn't valid.</summary>
    public string? LabelFor(string id) => Choices.FirstOrDefault(c => c.Id == id)?.Label ?? LabelOf?.Invoke(id);
}

public sealed record OptionTab(string Id, string Label, string Blurb, IReadOnlyList<OptionGroup> Groups);

/// <summary>Every option the randomizer offers, in display order. The window and the spoiler log both read this.</summary>
public static class OptionCatalog
{
    private static OptionGroup Toggle(string id, string title, string description, string? badge = null, bool implemented = false,
        string? warning = null, params SubOption[] subOptions) =>
        new(id, title, description, true, [], subOptions, warning, badge, implemented);

    private static OptionGroup Pick(string id, string title, string description, string? warning, params Choice[] choices) =>
        new(id, title, description, false, choices, [], warning);

    private static readonly Choice[] OrigShuffleRandom = [new("orig", "Original"), new("shuffle", "Shuffle"), new("random", "Randomize")];

    public static IReadOnlyList<OptionTab> Tabs { get; } =
    [
        new("world", "World", "How the story and the world are opened up.",
        [
            Toggle("openworld", "Open world",
                "Based on Bartz24's FF12 Open World Randomizer: the story no longer walls areas off. Key items like the aeropasses, the Writ of Transit and the story's nethicite open the way instead, and story events and hunts hand them out. Uses that randomizer's default key item layout; the spoiler log lists where each one is, under Open world rewards. Combine it freely with the other options.",
                badge: "Experimental", implemented: true,
                warning: "Experimental and untested: start a new game, and keep a save outside the randomized game you can go back to. Story scenes are skipped or changed, so expect rough edges. Thanks to Bartz24 for the open world (MIT licensed)."),
            Toggle("skipprologue", "Skip the prologue",
                "A new game skips the opening movie and Reks's prologue at Nalbina Fortress and starts where Vaan takes over, in the Garamsythe Waterway. Open world has its own start, so this does nothing with it on.",
                badge: "Untested", implemented: true),
        ]),
        new("party", "Party", "Starting stats, items and equipment for your party.",
        [
            Toggle("potions", "Vaan starts with 99 Potions",
                "A quick way to check the randomizer is working: start a new game and Vaan's Potions stack is 99 instead of 5.",
                badge: "Test mod", implemented: true),
            new("pstats", "Party stats", "The six main characters trade starting stats and stat growth: each takes another one's whole set, so Penelo might grow like Basch. Every value is one the game already uses.", false, [new("orig", "Original"), new("shuffle", "Shuffle between characters")], [], Badge: "Untested", Implemented: true),
            Toggle("startinv", "Starting equipment", "The weapons, armor and accessories each party member and guest brings when they join, swapped for gear of the same kind (a bow user still gets a bow and arrows). A party member wears a new piece only if they start with its license (the game takes unlicensed gear off); otherwise they keep the original on and get the new piece in the inventory. Items, magicks, technicks and gambits stay. With Keep things sane, replacements stay close in strength.", badge: "Untested", implemented: true,
                subOptions: new SubOption("startitems", "Starting items too", "Consumables they bring (Vaan's Potions, for example) become other consumables, in the same amounts")),
        ]),
        new("licenses", "License Board", "The Zodiac job boards, Espers and Quickenings.",
        [
            Pick("boards", "License boards",
                "What sits on each of the 12 job boards. Shuffle rearranges each board's own licenses; Randomize also mixes licenses between boards, so a job can end up with another job's weapons or magicks. Board shapes don't change, and Essentials, Espers and the second-board licenses stay in their cells, so every board stays fully learnable.",
                null, OrigShuffleRandom) with { Badge = "Untested", Implemented = true },
            Toggle("espers", "Espers", "The Esper licenses swap places with each other, across all boards, so each Esper is learned from different cells and boards.",
                badge: "Untested", implemented: true),
            Toggle("quickenings", "Quickenings", "The Quickening licenses swap places with each other across all boards.",
                badge: "Untested", implemented: true),
        ]),
        new("items", "Treasure & Shops", "Treasure chests, shop stock and the bazaar.",
        [
            new("treasure", "Treasure chests",
                "What each chest holds, including the rare finds you get with the Diamond Armlet. Shuffle moves the game's own chest contents between chests; Randomize rolls new items. With Keep things sane, items stay close to the area's place in the story. Key items and gil-only chests never change.",
                false, OrigShuffleRandom, [new("treasuregil", "Random gil amounts", "Chests that give gil give a different amount")],
                Implemented: true),
            new("shops", "Shops",
                "What each shop sells. Weapons stay weapons, magicks stay magicks and so on, and each shop keeps how many items it sells at each point in the story. Shuffle moves the game's own stock between shops; Randomize picks new items. With Keep things sane, every magick and technick stays on sale somewhere. Gambits don't change.",
                false, OrigShuffleRandom, [new("shopprices", "Random prices", "Consumables, equipment, magicks and technicks cost a different amount")],
                Implemented: true),
            new("bazaar", "Bazaar",
                "What each bazaar package gives you. The loot that unlocks a package and its price stay the same; only the items you get change. Shuffle moves the game's own package contents around; Randomize picks new items. Key items like the Monographs and Canopic Jar never move.",
                false, OrigShuffleRandom, [], Badge: "Untested", Implemented: true),
        ]),
        new("enemies", "Enemies", "Rewards, stats and weaknesses of the enemies you fight.",
        [
            new("rewards", "Enemy rewards",
                "What enemies give you. Shuffle moves the game's own rewards between enemies; Randomize picks new items. Every enemy of the same kind gets the same rewards, wherever it appears. With Keep things sane, loot stays loot and other items stay close in strength. Gil steals don't change.",
                false, OrigShuffleRandom,
                [
                    new("drops", "Drops", "All four drop tiers, plus the Monograph and Canopic Jar bonus drops"),
                    new("steals", "Steals", "What Steal can take"),
                    new("poaches", "Poaches", "What Poach gets you"),
                ],
                Warning: "Randomize can leave a piece of loot that only one enemy carried unobtainable, which locks the bazaar items that need it. Shuffle keeps every item obtainable.",
                Badge: "Untested", Implemented: true),
            new("estats", "Enemy stats",
                "How strong each kind of enemy is: HP, MP, Strength, Magick, Vitality, Speed, Evade, Defense, Magick Resist and Attack each change by their own amount, the same for that enemy everywhere. With Keep things sane, HP and MP stay within 80% to 125% and other stats within 85% to 120%; without, 50% to 200% and 60% to 160%. EXP, LP and gil don't change.",
                false, [new("orig", "Original"), new("random", "Randomize")], [], Badge: "Untested", Implemented: true),
            Toggle("elements", "Elemental affinities", "Each kind of enemy gets its elements reshuffled: what it absorbs, shrugs off, takes half damage from, is weak to and powers up all move to other elements together, so an enemy keeps the shape of its affinities with new elements. With Keep things sane, Holy and Dark stay as they are, so undead stay weak to Holy.", badge: "Untested", implemented: true),
        ]),
        new("gear", "Gear", "Weapon and armor stats.",
        [
            Toggle("wstats", "Weapon stats", "Each weapon's attack power, and ammunition's, changes around its original. With Keep things sane it stays within -25% and +30%; without, anywhere from 40% to 200%. Gear you already own changes too.", badge: "Untested", implemented: true),
            Toggle("astats", "Armor stats", "Defense and magick resist on body and head armor, and evade and magick evade on shields, change around their original values. With Keep things sane they stay within -25% and +30%; without, anywhere from 40% to 200%. Accessories keep their bonuses.", badge: "Untested", implemented: true),
        ]),
        new("general", "General", "Settings that apply to the whole randomizer.",
        [
            Toggle("sane", "Keep things sane", "Keeps random values within sensible limits so the game stays playable.", badge: "Recommended", implemented: true),
            Toggle("spoiler", "Spoiler log", "Saves a file listing everything that was changed. Open it from History any time.", implemented: true),
        ]),
    ];

    public static IEnumerable<OptionGroup> AllGroups => Tabs.SelectMany(t => t.Groups);
}
