using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SafeDiskCleaner.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
        : this("", "", "OK")
    {
    }

    public ConfirmDialog(string title, string message, string confirmButton)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmButton;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}