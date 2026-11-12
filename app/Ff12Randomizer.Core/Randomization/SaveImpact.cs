namespace Ff12Randomizer.Core.Randomization;

/// <summary>Whether an option reaches a game already in progress, or only one started after randomizing.</summary>
public enum SaveImpact
{
    /// <summary>The game reads this data live, so loading an existing save picks it up.</summary>
    ExistingSaves,
    /// <summary>Some of it is copied into the save, the rest applies right away.</summary>
    Partly,
    /// <summary>Copied into the save at New Game, so only a new game sees it.</summary>
    NewGameOnly,
}

public sealed record SaveImpactInfo(SaveImpact Impact, string Note)
{
    public string Label => Impact switch
    {
        SaveImpact.ExistingSaves => "Works on existing saves",
        SaveImpact.Partly => "Partly needs a new game",
        _ => "New game only",
    };
}

/// <summary>Which options need a new game. One table, so it is easy to correct as in-game testing confirms each entry.</summary>
public static class SaveImpacts
{
    private static readonly Dictionary<string, SaveImpactInfo> ByGroup = new()
    {
        ["openworld"] = new(SaveImpact.NewGameOnly, "The story's scripts change from the very start, so begin a new game."),
        ["skipprologue"] = new(SaveImpact.NewGameOnly, "The prologue only plays at the start of a new game. Saves aren't affected."),
        ["potions"] = new(SaveImpact.NewGameOnly, "Starting items are given when you start a new game."),
        ["pstats"] = new(SaveImpact.NewGameOnly, "Party stats are copied into your save when a character joins."),
        ["startinv"] = new(SaveImpact.NewGameOnly, "Starting gear (and items, if ticked) is given when a character joins."),
        ["boards"] = new(SaveImpact.NewGameOnly, "Licenses you have already learned are stored in your save."),
        ["espers"] = new(SaveImpact.NewGameOnly, "Licenses you have already learned are stored in your save."),
        ["quickenings"] = new(SaveImpact.NewGameOnly, "Licenses you have already learned are stored in your save."),
        ["treasure"] = new(SaveImpact.ExistingSaves, "Chests you haven't opened yet give the new items."),
        ["shops"] = new(SaveImpact.ExistingSaves, "Shops are read when you open them."),
        ["bazaar"] = new(SaveImpact.ExistingSaves, "The bazaar is read when you open it."),
        ["rewards"] = new(SaveImpact.ExistingSaves, "Enemy data is read when enemies appear."),
        ["estats"] = new(SaveImpact.ExistingSaves, "Enemy data is read when enemies appear."),
        ["elements"] = new(SaveImpact.ExistingSaves, "Enemy data is read when enemies appear."),
        ["wstats"] = new(SaveImpact.ExistingSaves, "Gear stats are read from the game data."),
        ["astats"] = new(SaveImpact.ExistingSaves, "Gear stats are read from the game data."),
    };

    /// <summary>Null for options that change nothing in the game (like the spoiler log).</summary>
    public static SaveImpactInfo? For(string groupId) => ByGroup.GetValueOrDefault(groupId);

    /// <summary>Titles of the options switched on in <paramref name="settings"/> that need a new game, fully or partly.</summary>
    public static IReadOnlyList<string> NeedingNewGame(RandomizerSettings settings) =>
        OptionCatalog.AllGroups
            .Where(g => g.Implemented && settings.IsActive(g) && For(g.Id) is { Impact: not SaveImpact.ExistingSaves })
            .Select(g => g.Title)
            .ToList();
}
