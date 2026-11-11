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
        new("party", "Party", "Starting stats, items and equipment for your party.",
        [
            Toggle("potions", "Vaan starts with 99 Potions",
                "A quick way to check the randomizer is working: start a new game and Vaan's Potions stack is 99 instead of 5.",
                badge: "Test mod", implemented: true),
            Pick("pstats", "Party stats", "Starting stats for every party member.", null, OrigShuffleRandom),
            Toggle("startinv", "Starting equipment and items", "Each character joins with random gear and items."),
        ]),
        new("licenses", "License Board", "The Zodiac job boards, Espers and Quickenings.",
        [
            Pick("boards", "License boards",
                "What sits on each of the 12 job boards. Shuffle moves each board's licenses around; Randomize builds boards from any licenses. Every board stays completable.",
                null, OrigShuffleRandom),
            Toggle("espers", "Espers", "Which board unlocks each Esper, and where on the board it sits."),
            Toggle("quickenings", "Quickenings", "Which characters' boards hold each Quickening."),
        ]),
        new("items", "Treasure & Shops", "Treasure chests, shop stock and the bazaar.",
        [
            Toggle("treasure", "Treasure chests",
                "What each chest holds, including gil amounts and the separate Diamond Armlet tables.", subOptions:
            [
                new("treasurerare", "Include rare treasure", "Chests that normally hold one-of-a-kind items"),
            ]),
            Toggle("shops", "Shops", "Each shop stocks a random selection of items and gear.", subOptions:
            [
                new("shopprices", "Random prices", "Prices change too"),
            ]),
            Toggle("bazaar", "Bazaar", "What the bazaar offers for your loot."),
        ]),
        new("enemies", "Enemies", "Rewards, stats and weaknesses of the enemies you fight.",
        [
            Toggle("rewards", "Enemy rewards", "What enemies give you.", subOptions:
            [
                new("drops", "Drops", "Normal and rare drops"),
                new("steals", "Steals", "What Steal can take"),
                new("poaches", "Poaches", "What Poach gets you"),
            ]),
            Pick("estats", "Enemy stats", "How strong individual enemies are.", null, OrigShuffleRandom),
            Toggle("elements", "Elemental affinities", "Enemy weaknesses, resistances, immunities and absorptions are re-rolled."),
        ]),
        new("gear", "Gear", "Weapon and armor stats.",
        [
            Toggle("wstats", "Weapon stats", "Attack power and other weapon stats are randomized."),
            Toggle("astats", "Armor stats", "Defense, magick resist and bonuses on armor and accessories are randomized."),
        ]),
        new("general", "General", "Settings that apply to the whole randomizer.",
        [
            Toggle("sane", "Keep things sane", "Keeps random values within sensible limits so the game stays playable.", badge: "Recommended", implemented: true),
            Toggle("spoiler", "Spoiler log", "Saves a file listing everything that was changed. Open it from History any time.", implemented: true),
        ]),
    ];

    public static IEnumerable<OptionGroup> AllGroups => Tabs.SelectMany(t => t.Groups);
}
