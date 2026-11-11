using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ff12Randomizer.Core.Storage;

namespace Ff12Randomizer.App.ViewModels;

/// <summary>One spoiler section in the viewer's list, with how many rows match the search.</summary>
public sealed partial class SpoilerSectionViewModel : ObservableObject
{
    private readonly string[] _rowText;

    public SpoilerSectionViewModel(SpoilerTable table)
    {
        Title = table.Title;
        Table = new DataTable(table.Key);
        // Columns get plain names so the grid's bindings work with any title; the title goes in the caption.
        for (int c = 0; c < table.Columns.Count; c++) Table.Columns.Add($"c{c}", typeof(string)).Caption = table.Columns[c];
        foreach (var row in table.Rows) Table.Rows.Add(row);
        _rowText = table.Rows.Select(r => string.Join(" ", r).ToLowerInvariant()).ToArray();
        View = Table.DefaultView;
        MatchCount = _rowText.Length;
    }

    public string Title { get; }
    public DataTable Table { get; }
    public DataView View { get; }

    [ObservableProperty] private int _matchCount;
    [ObservableProperty] private bool _isSelected;

    public string CountLabel => MatchCount.ToString("N0");
    partial void OnMatchCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    /// <summary>Shows rows containing every word of <paramref name="terms"/> (any column, any case).</summary>
    public void Filter(string[] terms)
    {
        MatchCount = terms.Length == 0 ? _rowText.Length : _rowText.Count(t => terms.All(t.Contains));
        View.RowFilter = terms.Length == 0 ? "" : string.Join(" AND ", terms.Select(term =>
            "(" + string.Join(" OR ", Table.Columns.Cast<DataColumn>().Select(c => $"{c.ColumnName} LIKE '%{Escape(term)}%'")) + ")"));
    }

    /// <summary>DataView LIKE patterns: quotes doubled, wildcard characters bracketed.</summary>
    private static string Escape(string term) => string.Concat(term.Select(ch => ch switch
    {
        '\'' => "''",
        '*' or '%' or '[' or ']' => $"[{ch}]",
        _ => ch.ToString(),
    }));
}

/// <summary>The spoiler log, inside the app: sections on the left, a sortable table, and one search box over everything.</summary>
public sealed partial class SpoilerViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly string _path;
    private readonly object? _returnTo;

    /// <param name="returnTo">The screen to go back to on close (e.g. the finished run screen), or null for the main window.</param>
    public SpoilerViewModel(MainViewModel main, string path, object? returnTo)
    {
        _main = main;
        _path = path;
        _returnTo = returnTo;
        var doc = SpoilerDocument.Load(path);
        Sections = new(doc.Tables.Select(t => new SpoilerSectionViewModel(t)));
        // Checking a section any way (click, keyboard, accessibility tools) selects it.
        foreach (var section in Sections)
            section.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SpoilerSectionViewModel.IsSelected) && section.IsSelected) Selected = section; };
        Summary = doc.Summary.Count == 0 ? "" : string.Join(" · ", doc.Summary);
        Subtitle = $"Seed {doc.Seed}" + (doc.CreatedAt is { } when ? $" · {Format.When(when)}" : "");
        Selected = Sections.FirstOrDefault();
    }

    public ObservableCollection<SpoilerSectionViewModel> Sections { get; }
    public string Subtitle { get; }
    public string Summary { get; }
    public bool IsEmpty => Sections.Count == 0;

    [ObservableProperty] private SpoilerSectionViewModel? _selected;
    [ObservableProperty] private string _search = "";

    public string ResultLabel => Selected is null ? "" : string.IsNullOrWhiteSpace(Search)
        ? Rows(Selected.MatchCount)
        : $"{Selected.MatchCount:N0} of {Rows(Selected.Table.Rows.Count)} match";

    private static string Rows(int n) => n == 1 ? "1 row" : $"{n:N0} rows";

    partial void OnSelectedChanged(SpoilerSectionViewModel? oldValue, SpoilerSectionViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(ResultLabel));
    }

    partial void OnSearchChanged(string value)
    {
        var terms = value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var section in Sections) section.Filter(terms);
        // Jump to a section that has matches when the current one has none.
        if (Selected is { MatchCount: 0 } && Sections.FirstOrDefault(s => s.MatchCount > 0) is { } hit) Selected = hit;
        OnPropertyChanged(nameof(ResultLabel));
    }

    [RelayCommand] private void Select(SpoilerSectionViewModel section) => Selected = section;
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void OpenFile() => _main.OpenFile(_path);
    [RelayCommand] private void Close() => _main.Overlay = _returnTo;
}
