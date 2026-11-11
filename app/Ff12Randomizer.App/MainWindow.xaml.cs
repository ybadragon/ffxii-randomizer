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
}
