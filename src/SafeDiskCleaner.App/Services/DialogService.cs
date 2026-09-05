using Microsoft.Win32;

using SafeDiskCleaner.ViewModels.Abstractions;

namespace SafeDiskCleaner.App.Services;

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

    public Task<string?> PickSaveFileAsync(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            AddExtension = true,
            DefaultExt = ".csv",
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}

public sealed record ConfirmDialogModel(string Title, string Message, string ConfirmButton);
