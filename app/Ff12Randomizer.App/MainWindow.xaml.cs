using System.Data;
using System.Windows;
using System.Windows.Controls;
using Ff12Randomizer.App.ViewModels;
using MahApps.Metro.Controls;

namespace Ff12Randomizer.App;

public partial class MainWindow : MetroWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    /// <summary>Spoiler viewer columns: the section's own column titles (kept in the captions), wrapping long lists.</summary>
    private void SpoilerGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (sender is not DataGrid { ItemsSource: DataView view } || e.Column is not DataGridTextColumn column) return;
        column.Header = view.Table?.Columns[e.PropertyName]?.Caption ?? e.PropertyName;
        column.MaxWidth = 460;
        var wrap = new Style(typeof(TextBlock));
        wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        column.ElementStyle = wrap;
    }

    /// <summary>The viewer opens ready to type a search.</summary>
    private void SpoilerSearch_Loaded(object sender, RoutedEventArgs e) => ((UIElement)sender).Focus();
}
