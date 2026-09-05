namespace SafeDiskCleaner.ViewModels.Abstractions;

/// <summary>File/folder pickers and confirmations; implemented per UI framework.</summary>
public interface IDialogService
{
    Task<string[]?> PickFoldersAsync(string title);

    Task<bool> ConfirmAsync(string title, string message, string confirmButton = "OK");

    Task<string?> PickSaveFileAsync(string title, string defaultFileName, string filter);
}
