using System.Windows.Controls;
using SafeDiskCleaner.ViewModels;

namespace SafeDiskCleaner.App.Views;

public partial class AppsView : UserControl
{
    public AppsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is AppsViewModel viewModel)
            {
                await viewModel.RefreshAsync();
            }
        };
    }
}
