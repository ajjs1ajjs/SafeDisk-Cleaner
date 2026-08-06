using System.Windows;
using SafeDiskCleaner.App.ViewModels;

namespace SafeDiskCleaner.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
