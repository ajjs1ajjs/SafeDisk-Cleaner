using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.App.ViewModels;

public sealed partial class AuditViewModel : ObservableObject
{
    private readonly IAuditService _audit;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private string? _message;

    public ObservableCollection<AuditEntry> Entries { get; } = [];

    public AuditViewModel(IAuditService audit, IDialogService dialogs)
    {
        _audit = audit;
        _dialogs = dialogs;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var entries = await _audit.GetAllAsync();
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            Message = $"Помилка: {ex.Message}";
        }
    }

    public bool IsEmpty => Entries.Count == 0;

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (!await _dialogs.ConfirmAsync("Очистити лог", "Всі записи аудиту буде видалено. Продовжити?", "Очистити"))
        {
            return;
        }

        await _audit.ClearAsync();
        await RefreshAsync();
    }
}
