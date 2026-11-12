using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ff12Randomizer.Core;
using Ff12Randomizer.Core.Game;
using Ff12Randomizer.Core.Patching;
using Ff12Randomizer.Core.Randomization;
using Ff12Randomizer.Core.Storage;
using Ff12Randomizer.Core.Updates;
using Microsoft.Win32;

namespace Ff12Randomizer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths = AppPaths.Default;
    private readonly AppSettings _appSettings;
    private readonly PresetStore _presets;
    private readonly HistoryStore _history;
    private RandomizerService? _service;
    private RandomizerSettings _baseline;

    public MainViewModel()
    {
        _appSettings = AppSettings.Load(_paths);
        _presets = new PresetStore(_paths);
        _history = new HistoryStore(_paths);

        var groups = new List<GroupViewModel>();
        Tabs = [];
        foreach (var tab in OptionCatalog.Tabs)
        {
            var tabGroups = tab.Groups.Select(g => new GroupViewModel(g, OnSettingsChanged)).ToList();
            groups.AddRange(tabGroups);
            Tabs.Add(new TabViewModel(tab.Id, tab.Label, tab.Blurb, tabGroups, TabKind.Options, SelectTab));
        }
        Tabs.Add(new TabViewModel("presets", "Presets",
            "Load, rename and organize saved settings. Presets keep settings only, so each run gets a new seed unless you set one.", [], TabKind.Presets, SelectTab));
        Tabs.Add(new TabViewModel("history", "History",
            "Every run is saved here with its exact seed and settings, so any game you have played can be made again.", [], TabKind.History, SelectTab));
        _groups = groups;

        LoadedPreset = _appSettings.LastPresetId is { } id ? _presets.Find(id) : null;
        ApplyToControls(_appSettings.LastSettings ?? LoadedPreset?.Settings ?? RandomizerSettings.Defaults());
        _baseline = _appSettings.LastPresetBaseline ?? LoadedPreset?.Settings ?? CurrentSettings();
        SelectTab(Tabs[0]);
        ConnectGame(_appSettings.GameFolder is { } folder ? GameInstall.FromFolder(folder) : GameInstall.FindSteamInstall());
        RefreshLists();
        _ = CheckForUpdatesAsync();
    }

    private readonly IReadOnlyList<GroupViewModel> _groups;

    public string Version { get; } = $"v{AppInfo.Version}";

    /// <summary>A newer release on GitHub, found once at startup; null when up to date or offline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateLabel), nameof(UpdateTooltip))]
    private UpdateInfo? _update;

    public bool HasUpdate => Update is not null;
    public string UpdateLabel => Update is null ? "" : $"Update available: v{Update.Version}";
    public string UpdateTooltip => Update is null ? "" :
        $"{Update.Name} is out. Opens the download page; replace your copy of FFXII Randomizer with the new exe.";

    private async Task CheckForUpdatesAsync() => Update = await UpdateChecker.CheckAsync(AppInfo.Version);

    [RelayCommand]
    private void OpenUpdate() =>
        Process.Start(new ProcessStartInfo(Update?.PageUrl ?? UpdateChecker.ReleasesPage) { UseShellExecute = true });

    public ObservableCollection<TabViewModel> Tabs { get; }
    public ObservableCollection<PresetItemViewModel> MyPresets { get; } = [];
    public ObservableCollection<PresetItemViewModel> BuiltInPresets { get; } = [];
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptionsTab), nameof(IsPresetsTab), nameof(IsHistoryTab))]
    private TabViewModel? _selectedTab;

    public bool IsOptionsTab => SelectedTab?.Kind == TabKind.Options;
    public bool IsPresetsTab => SelectedTab?.Kind == TabKind.Presets;
    public bool IsHistoryTab => SelectedTab?.Kind == TabKind.History;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetLabel))]
    private Preset? _loadedPreset;

    [ObservableProperty] private bool _isModified;
    public string PresetLabel => LoadedPreset?.Name ?? "None";

    [ObservableProperty] private bool _useFixedSeed;
    [ObservableProperty] private string _seedText = "";

    [ObservableProperty] private string _gameTitle = "FINAL FANTASY XII THE ZODIAC AGE";
    [ObservableProperty] private string _gameDetail = "Looking for your Steam copy";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsRandomized;
    [ObservableProperty] private bool _gameFound;

    /// <summary>The run screen, a problem message or a name prompt shown over the window; null when none.</summary>
    [ObservableProperty] private object? _overlay;
    public bool HasOverlay => Overlay is not null;
    partial void OnOverlayChanged(object? value) => OnPropertyChanged(nameof(HasOverlay));

    // ---------- tabs and settings ----------

    private void SelectTab(TabViewModel tab)
    {
        if (SelectedTab == tab) return;
        SelectedTab = tab;
        foreach (var t in Tabs) t.IsSelected = t == tab;
    }

    public RandomizerSettings CurrentSettings()
    {
        var settings = new RandomizerSettings();
        foreach (var group in _groups) group.Save(settings);
        return settings;
    }

    private void ApplyToControls(RandomizerSettings settings)
    {
        foreach (var group in _groups) group.Load(settings);
        OnSettingsChanged();
    }

    private void OnSettingsChanged()
    {
        if (Tabs is null || _groups is null) return;
        var settings = CurrentSettings();
        foreach (var tab in Tabs.Where(t => t.Kind == TabKind.Options))
        {
            int count = tab.Groups.Count(g => settings.IsActive(g.Group));
            tab.CountLabel = count > 0 ? $"{count} on" : null;
        }
        IsModified = LoadedPreset is not null && _baseline is not null && !settings.SameAs(_baseline);
        _appSettings.LastSettings = settings;
        SaveAppSettings();
    }

    private void SaveAppSettings()
    {
        _appSettings.LastPresetId = LoadedPreset?.Id;
        _appSettings.LastPresetBaseline = LoadedPreset is null ? null : _baseline;
        try { _appSettings.Save(_paths); } catch (IOException) { }
    }

    // ---------- presets ----------

    public void LoadPreset(Preset preset)
    {
        LoadedPreset = preset;
        _baseline = preset.Settings.Clone();
        ApplyToControls(preset.Settings);
        UseFixedSeed = false;
        RefreshLists();
    }

    [RelayCommand]
    public void UpdateLoadedPreset()
    {
        if (LoadedPreset is not { BuiltIn: false } preset) return;
        var settings = CurrentSettings();
        _presets.Update(preset.Id, settings);
        _baseline = settings.Clone();
        OnSettingsChanged();
        RefreshLists();
    }

    /// <summary>Saves the current settings under a new name and loads it. Returns null if the name is unusable.</summary>
    public Preset? SaveNewPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var preset = _presets.Add(name, CurrentSettings());
        LoadedPreset = preset;
        _baseline = preset.Settings.Clone();
        OnSettingsChanged();
        RefreshLists();
        return preset;
    }

    [RelayCommand]
    private void PromptNewPreset()
    {
        Overlay = new NamePromptViewModel("Save current settings as a new preset", "Save preset",
            LoadedPreset is { } p ? $"{p.Name} copy" : "",
            name =>
            {
                if (SaveNewPreset(name) is null) return false;
                CloseOverlay();
                return true;
            },
            CloseOverlay);
    }

    public void DuplicatePreset(Preset preset)
    {
        _presets.Duplicate(preset.Id);
        RefreshLists();
    }

    public bool RenamePreset(Preset preset, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            _presets.Rename(preset.Id, name);
        }
        catch (InvalidOperationException e)
        {
            ShowProblem("Pick another name", e.Message, ProblemTone.Warning, "OK", CloseOverlay);
            return false;
        }
        OnPropertyChanged(nameof(PresetLabel));
        RefreshLists();
        return true;
    }

    public void ConfirmDeletePreset(Preset preset)
    {
        ShowProblem($"Delete \"{preset.Name}\"?", "The preset is removed. Runs made with it stay in History.", ProblemTone.Warning,
            "Delete preset", () =>
            {
                _presets.Delete(preset.Id);
                if (LoadedPreset?.Id == preset.Id) LoadedPreset = null;
                OnSettingsChanged();
                RefreshLists();
                CloseOverlay();
            }, "Keep it", CloseOverlay);
    }

    // ---------- history ----------

    public void UseHistorySettings(HistoryEntry entry, bool replaySeed)
    {
        var preset = entry.PresetId is { } id ? _presets.Find(id) : null;
        LoadedPreset = preset;
        _baseline = preset?.Settings.Clone() ?? entry.Settings.Clone();
        ApplyToControls(entry.Settings);
        UseFixedSeed = replaySeed;
        SeedText = replaySeed ? entry.Seed : "";
        SelectTab(Tabs[0]);
        RefreshLists();
    }

    public void DeleteHistory(HistoryEntry entry)
    {
        _history.Delete(entry.Id);
        RefreshLists();
    }

    private void RefreshLists()
    {
        MyPresets.Clear();
        foreach (var p in _presets.Mine) MyPresets.Add(new PresetItemViewModel(p, this));
        BuiltInPresets.Clear();
        foreach (var p in PresetStore.BuiltIns) BuiltInPresets.Add(new PresetItemViewModel(p, this));

        string? current = _service?.Patcher.GetStatus() == GameDataStatus.Randomized ? _service.Patcher.CurrentFingerprint() : null;
        History.Clear();
        foreach (var e in _history.Entries) History.Add(new HistoryItemViewModel(e, current is not null && e.PatchedFingerprint == current, this));

        Tabs.First(t => t.Kind == TabKind.Presets).CountLabel = MyPresets.Count > 0 ? MyPresets.Count.ToString() : null;
        Tabs.First(t => t.Kind == TabKind.History).CountLabel = History.Count > 0 ? History.Count.ToString() : null;
        SaveAppSettings();
    }

    // ---------- the game ----------

    private void ConnectGame(GameInstall? game)
    {
        _service = game is null ? null : new RandomizerService(game, _paths, _presets, _history);
        GameFound = game is not null;
        RefreshStatus();
        if (game is null) ShowGameNotFound();
    }

    private void RefreshStatus()
    {
        if (_service is null)
        {
            GameDetail = "Not found";
            StatusText = "";
            return;
        }
        GameDetail = _appSettings.GameFolder is null ? "Steam copy found automatically" : _service.Game.Directory;
        try
        {
            var status = _service.Patcher.GetStatus();
            StatusIsRandomized = status == GameDataStatus.Randomized;
            var applied = StatusIsRandomized ? _service.AppliedEntry() : null;
            StatusText = status switch
            {
                GameDataStatus.Randomized => "Randomized",
                GameDataStatus.OtherLoader => "Another mod loader is installed",
                _ => "Original game",
            };
            if (applied is not null) StatusText = $"Randomized · seed {applied.Seed}";
        }
        catch (IOException)
        {
            StatusText = "Game files busy";
        }
        RefreshListsSafe();
    }

    private void RefreshListsSafe()
    {
        if (MyPresets is not null) RefreshLists();
    }

    private void ShowGameNotFound() =>
        ShowProblem("Couldn't find FINAL FANTASY XII",
            "The Steam copy of FINAL FANTASY XII THE ZODIAC AGE wasn't found. Choose the game's folder, the one that contains FFXII_TZA.vbf.",
            ProblemTone.Warning, "Choose game folder", ChooseGameFolder);

    private void ChooseGameFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the FINAL FANTASY XII THE ZODIAC AGE folder (contains FFXII_TZA.vbf)" };
        if (dialog.ShowDialog() != true) return;
        var game = GameInstall.FromFolder(dialog.FolderName);
        if (game is null)
        {
            ShowProblem("That folder isn't the game", "Choose the folder that contains FFXII_TZA.vbf and an x64 folder with FFXII_TZA.exe.",
                ProblemTone.Warning, "Choose game folder", ChooseGameFolder);
            return;
        }
        _appSettings.GameFolder = dialog.FolderName;
        SaveAppSettings();
        CloseOverlay();
        ConnectGame(game);
    }

    private bool ReadyToChangeGame()
    {
        if (_service is null)
        {
            ShowGameNotFound();
            return false;
        }
        if (GameInstall.IsGameRunning())
        {
            ShowGameRunning();
            return false;
        }
        if (_service.Patcher.GetStatus() == GameDataStatus.OtherLoader)
        {
            ShowOtherLoader();
            return false;
        }
        if (!_service.Game.CanWriteGameFolder())
        {
            ShowProblem("Windows needs your permission",
                "Your game is installed in a protected folder. Restart FFXII Randomizer with permission to add its files there, then click Randomize again. The game itself never needs to run as administrator.",
                ProblemTone.Info, "Restart with permission", RestartElevated, "Cancel", CloseOverlay);
            return false;
        }
        return true;
    }

    private void ShowGameRunning() =>
        ShowProblem("FINAL FANTASY XII is running",
            "The randomizer can't install while the game or its settings tool is open. Nothing has been changed yet.",
            ProblemTone.Warning, "OK", CloseOverlay);

    private void ShowOtherLoader() =>
        ShowProblem("Another mod loader is installed",
            $"{_service?.Game.LoaderPath} belongs to another mod (often the External File Loader). FFXII Randomizer uses the same file name, so it won't replace it. Uninstall that mod first, then click Randomize again.",
            ProblemTone.Warning, "OK", CloseOverlay);

    private static void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The person clicked No on the Windows prompt; stay as we are.
        }
    }

    // ---------- randomize and restore ----------

    [RelayCommand]
    private async Task RandomizeAsync()
    {
        if (!ReadyToChangeGame()) return;
        var settings = CurrentSettings();
        var seed = UseFixedSeed && !string.IsNullOrWhiteSpace(SeedText) ? new Seed(SeedText.Trim()) : Seed.NewRandom();
        var preset = LoadedPreset;
        bool modified = IsModified;
        // A run only counts as "from" a preset if it used that preset's exact settings.
        var presetForHistory = preset is not null && !modified ? preset : null;

        var run = new RunViewModel(this, seed.Text);
        Overlay = run;
        var progress = new Progress<RunStep>(run.Report);
        try
        {
            var outcome = await Task.Run(() => _service!.Run(settings, seed, presetForHistory, progress));
            run.Finish(outcome, preset, modified);
        }
        catch (GameRunningException)
        {
            ShowGameRunning();
        }
        catch (OtherLoaderException)
        {
            ShowOtherLoader();
        }
        catch (PatchFailedException e)
        {
            ShowFailure(e.Message, e);
        }
        catch (ValidationFailedException e)
        {
            ShowFailure(e.Message, new Exception(string.Join(Environment.NewLine, e.Problems), e));
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ShowFailure(e.Message, e);
        }
        RefreshStatus();
    }

    private void ShowFailure(string message, Exception e) =>
        ShowProblem("Randomizing didn't finish", message, ProblemTone.Error, "OK", CloseOverlay,
            "Copy error details", () => System.Windows.Clipboard.SetText($"FFXII Randomizer {AppInfo.Version}\n{e}"));

    [RelayCommand]
    private void Restore()
    {
        if (_service is null)
        {
            ShowGameNotFound();
            return;
        }
        if (_service.Patcher.GetStatus() != GameDataStatus.Randomized)
        {
            ShowProblem("Your game is already original", "There's no randomization to remove.", ProblemTone.Info, "OK", CloseOverlay);
            return;
        }
        ShowProblem("Restore original game?",
            "This removes the randomizer's loader and its ff12rando folder from the game. The game's own files were never changed, so it plays exactly as Steam installed it. Your saves and your History aren't touched.",
            ProblemTone.Info, "Restore original", async () =>
            {
                if (!ReadyToChangeGame()) return;
                try
                {
                    await Task.Run(() => _service.Patcher.Restore());
                    CloseOverlay();
                }
                catch (Exception e) when (e is GameRunningException or IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    ShowFailure(e.Message, e);
                }
                RefreshStatus();
            }, "Keep randomized", CloseOverlay);
    }

    public void LaunchGame()
    {
        Process.Start(new ProcessStartInfo($"steam://rungameid/{GameInstall.SteamAppId}") { UseShellExecute = true });
        CloseOverlay();
    }

    public void OpenFile(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No program is set up to open .json files on this PC.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    /// <summary>
    /// History's "Update spoiler log": rebuilds a run's spoiler log with this version's names and sections, after checking
    /// this version makes exactly the game that run installed, then shows it. The game isn't touched.
    /// </summary>
    public async Task UpdateSpoilerAsync(HistoryEntry entry)
    {
        if (_service is null)
        {
            ShowGameNotFound();
            return;
        }
        try
        {
            string path = await Task.Run(() => _service.RefreshSpoiler(entry));
            RefreshLists();
            ShowSpoiler(path);
        }
        catch (SpoilerMismatchException e)
        {
            ShowProblem("Can't update this spoiler log", $"{e.Message} Its log is unchanged. For an up-to-date log, use Replay this seed: it installs this version's game for the same seed, with a new log.",
                ProblemTone.Info, "OK", CloseOverlay);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ShowProblem("Couldn't update the spoiler log", e.Message, ProblemTone.Warning, "OK", CloseOverlay);
        }
    }

    /// <summary>Opens a spoiler log in the app's viewer; if it can't be read, offers the file itself.</summary>
    public void ShowSpoiler(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            Overlay = new SpoilerViewModel(this, path, returnTo: Overlay);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            ShowProblem("Couldn't show the spoiler log", $"{e.Message} You can still open the file itself.", ProblemTone.Warning,
                "Open the file", () => { CloseOverlay(); OpenFile(path); }, "Cancel", CloseOverlay);
        }
    }

    public void CloseOverlay() => Overlay = null;

    private void ShowProblem(string title, string message, ProblemTone tone, string action, Action onAction,
        string? secondary = null, Action? onSecondary = null) =>
        Overlay = new ProblemViewModel(title, message, tone, action, onAction, secondary, onSecondary);
}
