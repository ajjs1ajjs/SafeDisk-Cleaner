using Avalonia.Controls;
using Avalonia.Platform.Storage;

using SafeDiskCleaner.ViewModels.Abstractions;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Avalonia-backed dialog service: uses the platform storage provider for
/// folder/save pickers and a small modal window for confirmation prompts.
/// </summary>
public sealed class DialogService : IDialogService
{
    public static Window? MainWindow { get; set; }

    public async Task<string[]?> PickFoldersAsync(string title)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
        });

        return folders
            .Select(f => f.TryGetLocalPath() ?? f.Name)
            .ToArray();
    }

    public Task<string?> PickSaveFileAsync(string title, string defaultFileName, string filter)
    {
        return PickSaveFileCoreAsync(title, defaultFileName);
    }

    private static async Task<string?> PickSaveFileCoreAsync(string title, string defaultFileName)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var ext = System.IO.Path.GetExtension(defaultFileName).TrimStart('.');
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = string.IsNullOrEmpty(ext) ? null : ext,
        });

        return file?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmButton = "OK")
    {
        var owner = MainWindow;
        if (owner is null)
        {
            return false;
        }

        var dialog = new Views.ConfirmDialog(title, message, confirmButton);
        return await dialog.ShowDialog<bool>(owner);
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (MainWindow is { } window)
        {
            return window.StorageProvider;
        }

        return null;
    }
}