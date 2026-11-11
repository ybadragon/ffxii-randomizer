using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ff12Randomizer.Core.Randomization;

namespace Ff12Randomizer.App.ViewModels;

public sealed partial class ChoiceViewModel(Choice choice, GroupViewModel owner) : ObservableObject
{
    public string Id => choice.Id;
    public string Label => choice.Label;
    public string GroupName => owner.Id;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) owner.Select(this);
    }
}

public sealed partial class SubOptionViewModel(SubOption option, Action changed) : ObservableObject
{
    public string Id => option.Id;
    public string Label => option.Label;
    public string Description => option.Description;
    public string? Warning => option.Warning;
    public bool HasWarning => option.Warning is not null;
    public bool Implemented => option.Implemented;
    public bool ComingSoon => !option.Implemented;

    [ObservableProperty] private bool _isOn;

    partial void OnIsOnChanged(bool value) => changed();
}

/// <summary>A feature box's second setting, picked from a list (shown only for some of the box's choices).</summary>
public sealed partial class DropdownViewModel(OptionDropdown dropdown, Action changed) : ObservableObject
{
    public string Id => dropdown.Id;
    public string Label => dropdown.Label;
    public string? Hint => dropdown.Hint;

    /// <summary>The listed choices, plus a valid value that isn't one of them.</summary>
    public System.Collections.ObjectModel.ObservableCollection<Choice> Choices { get; } = new(dropdown.Choices);

    /// <summary>Recording from a controller (as FFX's skip button does) isn't used by any FFXII option yet.</summary>
    public bool CanRecord => false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning))]
    private string _selectedId = dropdown.Choices[0].Id;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string? _recordStatus;

    /// <summary>A warning that depends on other settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning))]
    private string? _contextWarning;

    public string? Warning => dropdown.Warnings?.GetValueOrDefault(SelectedId) ?? ContextWarning;
    public bool HasWarning => Warning is not null;

    /// <summary>Selects a value, adding it to the list first when it is a valid one the list doesn't have.</summary>
    internal void Select(string id)
    {
        if (Choices.All(c => c.Id != id) && dropdown.LabelOf?.Invoke(id) is { } label) Choices.Add(new Choice(id, label));
        SelectedId = id;
    }

    internal bool Loading { get; set; }

    partial void OnSelectedIdChanged(string value)
    {
        if (!Loading) changed();
    }

    internal void ShowFor(string? choiceId) => IsVisible = choiceId is not null && dropdown.ShownFor.Contains(choiceId);

    [RelayCommand]
    private void Record() { }
}

/// <summary>One feature box in an options tab.</summary>
public sealed partial class GroupViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;

    public GroupViewModel(OptionGroup group, Action changed)
    {
        Group = group;
        _changed = changed;
        Choices = group.Choices.Select(c => new ChoiceViewModel(c, this)).ToList();
        SubOptions = group.SubOptions.Select(s => new SubOptionViewModel(s, Changed)).ToList();
        Dropdown = group.Dropdown is { } d ? new DropdownViewModel(d, Changed) : null;
        SaveImpact = SaveImpacts.For(group.Id);
    }

    public OptionGroup Group { get; }
    public string Id => Group.Id;
    public string Title => Group.Title;
    public string Description => Group.Description;
    public bool HasToggle => Group.HasToggle;
    public bool Implemented => Group.Implemented;
    public string? Badge => Group.Implemented ? Group.Badge : "Coming soon";
    public bool HasBadge => Badge is not null;
    public bool BadgeIsComingSoon => !Group.Implemented;

    /// <summary>Whether this option reaches a game already in progress; null for options that don't change the game.</summary>
    public SaveImpactInfo? SaveImpact { get; }
    public bool ShowsOnExistingSaves => Group.Implemented && SaveImpact?.Impact == Core.Randomization.SaveImpact.ExistingSaves;
    public bool ShowsPartly => Group.Implemented && SaveImpact?.Impact == Core.Randomization.SaveImpact.Partly;
    public bool ShowsNewGameOnly => Group.Implemented && SaveImpact?.Impact == Core.Randomization.SaveImpact.NewGameOnly;
    public IReadOnlyList<ChoiceViewModel> Choices { get; }
    public IReadOnlyList<SubOptionViewModel> SubOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBody), nameof(ShowChoices), nameof(ShowSubOptions), nameof(ShowWarning))]
    private bool _isOn;

    public bool ShowBody => !HasToggle || IsOn;
    public bool ShowChoices => ShowBody && Choices.Count > 0;
    public bool ShowSubOptions => ShowBody && SubOptions.Count > 0;
    public bool ShowWarning => ShowBody && Group.Warning is not null;
    public string? Warning => Group.Warning;

    partial void OnIsOnChanged(bool value)
    {
        // A toggled box's dropdown (the soft reset combo) shows while it is on.
        if (Choices.Count == 0 && Dropdown is not null) Dropdown.IsVisible = value;
        Changed();
    }

    /// <summary>The box's second setting, if it has one (shown only for some choices).</summary>
    public DropdownViewModel? Dropdown { get; }

    internal void Select(ChoiceViewModel selected)
    {
        foreach (var choice in Choices.Where(c => c != selected)) choice.IsSelected = false;
        Dropdown?.ShowFor(selected.Id);
        Changed();
    }

    public void Load(RandomizerSettings settings)
    {
        _loading = true;
        IsOn = settings.IsOn(Id);
        if (Choices.Count > 0)
        {
            string picked = settings.ChoiceOf(Group);
            foreach (var choice in Choices) choice.IsSelected = choice.Id == picked;
        }
        foreach (var sub in SubOptions) sub.IsOn = settings.IsOn(sub.Id);
        if (Dropdown is not null && Group.Dropdown is { } d)
        {
            Dropdown.Loading = true;
            Dropdown.Select(settings.ChoiceOf(d));
            Dropdown.RecordStatus = null;
            Dropdown.Loading = false;
            if (Choices.Count == 0) Dropdown.IsVisible = IsOn;
            else Dropdown.ShowFor(Choices.FirstOrDefault(c => c.IsSelected)?.Id);
        }
        _loading = false;
    }

    public void Save(RandomizerSettings settings)
    {
        if (HasToggle) settings.Toggles[Id] = IsOn;
        if (Choices.FirstOrDefault(c => c.IsSelected) is { } picked) settings.Choices[Id] = picked.Id;
        foreach (var sub in SubOptions) settings.Toggles[sub.Id] = sub.IsOn;
        if (Dropdown is not null) settings.Choices[Dropdown.Id] = Dropdown.SelectedId;
    }

    private void Changed()
    {
        if (!_loading) _changed();
    }
}

public sealed partial class TabViewModel(string id, string label, string blurb, IReadOnlyList<GroupViewModel> groups, TabKind kind,
    Action<TabViewModel> selected) : ObservableObject
{
    partial void OnIsSelectedChanged(bool value)
    {
        if (value) selected(this);
    }

    public string Id => id;
    public string Label => label;
    public string Blurb => blurb;
    public IReadOnlyList<GroupViewModel> Groups => groups;
    public TabKind Kind => kind;
    public bool StartsSection => kind == TabKind.Presets;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _countLabel;
}

public enum TabKind { Options, Presets, History }
