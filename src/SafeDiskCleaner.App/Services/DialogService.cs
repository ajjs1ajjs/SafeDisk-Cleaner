using Microsoft.Win32;

namespace SafeDiskCleaner.App.Services;

public interface IDialogService
{
    Task<string[]?> PickFoldersAsync(string title);
    Task<bool> ConfirmAsync(string title, string message, string confirmButton = "OK");
}

public sealed class DialogService : IDialogService
{
    public Task<string[]?> PickFoldersAsync(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = true,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderNames : null);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmButton = "OK")
    {
        var content = new ConfirmDialog
        {
            DataContext = new ConfirmDialogModel(title, message, confirmButton),
        };
        var result = await MaterialDesignThemes.Wpf.DialogHost.Show(content, "RootDialogHost");
        return result is true;
    }
}

public sealed record ConfirmDialogModel(string Title, string Message, string ConfirmButton);
