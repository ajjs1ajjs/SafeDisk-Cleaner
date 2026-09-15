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
        return PickSaveFileCoreAsync(title, defaultFileName, filter);
    }

    private static async Task<string?> PickSaveFileCoreAsync(string title, string defaultFileName, string? filter)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var ext = System.IO.Path.GetExtension(defaultFileName).TrimStart('.');
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = string.IsNullOrEmpty(ext) ? null : ext,
        };
        if (!string.IsNullOrWhiteSpace(filter))
        {
            // Map "Desc (*.csv)|*.csv|..." pipe types to Avalonia file-type
            // choices so the requested extension is actually enforced.
            var choices = new List<FilePickerFileType>();
            foreach (var part in filter.Split('|'))
            {
                var patterns = part.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => p.StartsWith("*."))
                    .Select(p => p.Trim())
                    .ToArray();
                if (patterns.Length > 0)
                {
                    choices.Add(new FilePickerFileType(part.Split('(')[0].Trim()) { Patterns = patterns.ToList() });
                }
            }

            if (choices.Count > 0)
            {
                options.FileTypeChoices = choices;
            }
        }

        var file = await storage.SaveFilePickerAsync(options);
        var picked = file?.TryGetLocalPath();
        // Post-pick enforcement: the platform may still allow "all files".
        if (picked is not null && !string.IsNullOrEmpty(ext)
            && !picked.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return picked;
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