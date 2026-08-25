using SafeDiskCleaner.Core.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.ViewModels.Abstractions;
using SafeDiskCleaner.ViewModels.Services;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Platform;
using SafeDiskCleaner.Core.Scanning;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.ViewModels;

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
            roots.AddRange(_scan.Drives.Select(d => d.RootPath()));
        }

        if (roots.Count == 0)
        {
            Message = Loc.T("Dup.NeedRoots");
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
            var result = await _scanner.ScanDuplicatesAsync(roots, _cts.Token, _settings.Exclusions);
            DuplicateResult = result;
            foreach (var candidate in result.Candidates)
            {
                var row = new CandidateRow(candidate);
                row.PropertyChanged += OnCandidatePropertyChanged;
                Candidates.Add(row);
            }

            Message = result.Candidates.Count == 0
                ? Loc.T("Dup.NotFound")
                : Loc.F("Dup.Found", result.Candidates.Count);
        }
        catch (OperationCanceledException)
        {
            Message = Loc.T("Dup.Cancelled");
        }
        catch (Exception ex)
        {
            Message = Loc.F("Common.Error", ex.Message);
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
            if (select)
            {
                foreach (var row in Candidates.Where(c => c.IsSelectable))
                {
                    row.IsSelected = true;
                }

                // Always keep the newest copy of every duplicate group so the
                // user can never accidentally delete the last copy of a file.
                foreach (var group in Candidates
                             .Where(c => c.GroupId.Length > 0)
                             .GroupBy(c => c.GroupId))
                {
                    var keeper = group
                        .OrderBy(c => c.LastAccessDays ?? uint.MaxValue)
                        .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                        .First();
                    keeper.IsSelected = false;
                }
            }
            else
            {
                foreach (var row in Candidates)
                {
                    row.IsSelected = false;
                }
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
            Message = Loc.T("Common.NothingSelected");
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
            Message = Loc.F("Common.ProcessedFreed", result.Processed, HumanSize.Format(result.FreedBytes));
            await _eventBus.RaiseDataChangedAsync();
        }
        catch (Exception ex)
        {
            Message = Loc.F("Cleanup.Error", ex.Message);
        }
        finally
        {
            IsCleaning = false;
        }
    }
}
