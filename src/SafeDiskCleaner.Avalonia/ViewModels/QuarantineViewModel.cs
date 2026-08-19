using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.App.ViewModels;

public sealed partial class QuarantineViewModel : ObservableObject
{
    private readonly IQuarantineService _quarantine;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _message;

    public ObservableCollection<QuarantineEntry> Entries { get; } = [];

    public QuarantineViewModel(IQuarantineService quarantine, IDialogService dialogs)
    {
        _quarantine = quarantine;
        _dialogs = dialogs;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var entries = await _quarantine.ListAsync();
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
    private async Task RestoreAsync(QuarantineEntry entry)
    {
        try
        {
            await _quarantine.RestoreAsync(entry.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Message = $"Не вдалося відновити: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(QuarantineEntry entry)
    {
        if (!await _dialogs.ConfirmAsync("Видалити з карантину", $"Файл буде видалено безповоротно:\n{entry.OriginalPath}", "Видалити"))
        {
            return;
        }

        try
        {
            await _quarantine.RemoveAsync(entry.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Message = $"Помилка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task EmptyAsync()
    {
        if (!await _dialogs.ConfirmAsync("Очистити карантин", "Усі карантинні файли буде видалено безповоротно. Продовжити?", "Очистити"))
        {
            return;
        }

        try
        {
            var count = await _quarantine.EmptyAsync();
            await RefreshAsync();
            Message = $"Карантин очищено ({count} записів).";
        }
        catch (Exception ex)
        {
            Message = $"Помилка: {ex.Message}";
        }
    }
}