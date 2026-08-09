using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Scanning;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.App.ViewModels;

public sealed partial class DuplicatesViewModel : ObservableObject
{
    private readonly Scanner _scanner;
    private readonly CleanupEngine _cleanup;
    private readonly IDialogService _dialogs;
    private readonly IAppEventBus _eventBus;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _cleanupCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private ScanResult? _duplicateResult;

    [ObservableProperty]
    private CleanupResult? _cleanupResult;

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    public long SelectedSize => Candidates.Where(c => c.IsSelected).Sum(c => c.Size);
    public int SelectedCount => Candidates.Count(c => c.IsSelected);

    public DuplicatesViewModel(
        Scanner scanner,
        CleanupEngine cleanup,
        IDialogService dialogs,
        IAppEventBus eventBus,
        AppSettings settings,
        ScanViewModel scanViewModel)
    {
        _scanner = scanner;
        _cleanup = cleanup;
        _dialogs = dialogs;
        _eventBus = eventBus;
        _settings = settings;
        _scan = scanViewModel;
    }

    private readonly ScanViewModel _scan;

    [RelayCommand]
    private async Task ScanDuplicatesAsync()
    {
        var roots = _scan.CustomRoots.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roots.Count == 0)
        {
            roots.AddRange(_scan.Drives.Select(d => $"{d.Letter}\\"));
        }

        if (roots.Count == 0)
        {
            Message = "Вкажіть шляхи для аналізу дублікатів.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsScanning = true;
        Candidates.Clear();
        DuplicateResult = null;
        CleanupResult = null;
        Message = null;

        try
        {
            var result = await Task.Run(() => _scanner.ScanDuplicates(roots, _cts.Token), _cts.Token);
            DuplicateResult = result;
            foreach (var candidate in result.Candidates)
            {
                var row = new CandidateRow(candidate);
                row.PropertyChanged += OnCandidatePropertyChanged;
                Candidates.Add(row);
            }

            Message = result.Candidates.Count == 0
                ? "Дублікатів не знайдено."
                : $"Знайдено дублікатів: {result.Candidates.Count}.";
        }
        catch (OperationCanceledException)
        {
            Message = "Пошук дублікатів скасовано.";
        }
        catch (Exception ex)
        {
            Message = $"Помилка: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void CancelScan() => _cts?.Cancel();

    public void ToggleCandidateSelection(CandidateRow row) => OnPropertyChanged(nameof(SelectedSize));

    private bool _batchUpdating;

    private void OnCandidatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CandidateRow.IsSelected) && !_batchUpdating)
        {
            OnPropertyChanged(nameof(SelectedSize));
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    [RelayCommand]
    public void ToggleAllSelection(bool select)
    {
        _batchUpdating = true;
        try
        {
            foreach (var row in Candidates.Where(c => c.IsSelectable))
            {
                row.IsSelected = select;
            }
        }
        finally
        {
            _batchUpdating = false;
        }

        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private async Task CleanDuplicatesAsync()
    {
        var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Item).ToList();
        if (selected.Count == 0)
        {
            Message = "Нічого не вибрано.";
            return;
        }

        IsCleaning = true;
        Message = null;
        try
        {
            var options = new CleanupOptions
            {
                Mode = CleanMode.Interactive,
                QuarantineRetentionDays = _settings.QuarantineRetentionDays,
                MoveToRecycleBin = _settings.MoveToRecycleBin,
                AutoThreshold = _settings.AutoThreshold,
            };

            _cleanupCts?.Cancel();
            _cleanupCts = new CancellationTokenSource();

            var result = await Task.Run(
                () => _cleanup.RunAsync(selected, options, _ => { }, _cleanupCts.Token),
                _cleanupCts.Token);
            CleanupResult = result;
            Message = $"Оброблено {result.Processed}, звільнено {HumanSize.Format(result.FreedBytes)}.";
            await _eventBus.RaiseDataChangedAsync();
        }
        catch (Exception ex)
        {
            Message = $"Помилка очищення: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
        }
    }
}
