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

public sealed partial class ScanViewModel : ObservableObject
{
    private readonly Scanner _scanner;
    private readonly CleanupEngine _cleanup;
    private readonly IDialogService _dialogs;
    private readonly IAppEventBus _eventBus;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private ObservableCollection<DriveInfo> _drives = [];

    [ObservableProperty]
    private string _customRoots;

    [ObservableProperty]
    private bool _includeMedium;

    [ObservableProperty]
    private bool _includeAdvanced;

    [ObservableProperty]
    private byte _minConfidence = 50;

    [ObservableProperty]
    private uint _recencyDays = 3;

    [ObservableProperty]
    private bool _moveToRecycleBin = true;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private ScanProgress? _scanProgress;

    [ObservableProperty]
    private CleanupProgress? _cleanupProgress;

    [ObservableProperty]
    private ScanResult? _scanResult;

    [ObservableProperty]
    private CleanupResult? _cleanupResult;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string _dataRoot = string.Empty;

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    public long SelectedSize => Candidates.Where(c => c.IsSelected).Sum(c => c.Size);
    public int SelectedCount => Candidates.Count(c => c.IsSelected);

    public ScanViewModel(
        Scanner scanner,
        CleanupEngine cleanup,
        IDialogService dialogs,
        IAppEventBus eventBus,
        AppSettings settings,
        Core.Abstractions.IAppPaths paths)
    {
        _scanner = scanner;
        _cleanup = cleanup;
        _dialogs = dialogs;
        _eventBus = eventBus;
        _settings = settings;

        _customRoots = settings.CustomRoots;
        _includeMedium = settings.IncludeMedium;
        _includeAdvanced = settings.IncludeAdvanced;
        _minConfidence = settings.MinConfidence;
        _recencyDays = settings.RecencyDays;
        _moveToRecycleBin = settings.MoveToRecycleBin;
        DataRoot = paths.DataRoot;

        Drives = new ObservableCollection<DriveInfo>(Core.Windows.WindowsApi.ListDrives());
    }

    partial void OnMinConfidenceChanged(byte value) => OnOptionsChanged();
    partial void OnRecencyDaysChanged(uint value) => OnOptionsChanged();
    partial void OnIncludeMediumChanged(bool value) => OnOptionsChanged();
    partial void OnIncludeAdvancedChanged(bool value) => OnOptionsChanged();
    partial void OnMoveToRecycleBinChanged(bool value) => OnOptionsChanged();

    private void OnOptionsChanged() => SaveSettings();

    private void SaveSettings()
    {
        _settings.CustomRoots = CustomRoots;
        _settings.IncludeMedium = IncludeMedium;
        _settings.IncludeAdvanced = IncludeAdvanced;
        _settings.MinConfidence = MinConfidence;
        _settings.RecencyDays = RecencyDays;
        _settings.MoveToRecycleBin = MoveToRecycleBin;
        _settings.Save();
    }

    /// <summary>Applies preferences loaded from disk (after all VMs are constructed).</summary>
    public void LoadSavedOptions()
    {
        CustomRoots = _settings.CustomRoots;
        IncludeMedium = _settings.IncludeMedium;
        IncludeAdvanced = _settings.IncludeAdvanced;
        MinConfidence = _settings.MinConfidence;
        RecencyDays = _settings.RecencyDays;
        MoveToRecycleBin = _settings.MoveToRecycleBin;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void ToggleCandidateSelection(CandidateRow row) => NotifySelectionChanged();

    private void OnCandidatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CandidateRow.IsSelected))
        {
            NotifySelectionChanged();
        }
    }

    [RelayCommand]
    public void ToggleAllSelection(bool select)
    {
        foreach (var row in Candidates.Where(c => c.IsSelectable))
        {
            row.IsSelected = select;
        }

        NotifySelectionChanged();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        SaveSettings();

        var options = new ScanOptions
        {
            Roots = ParseRoots(CustomRoots),
            IncludeMedium = IncludeMedium,
            IncludeAdvanced = IncludeAdvanced,
            MinConfidence = MinConfidence,
            RecencyDays = RecencyDays,
        };

        var validator = new Core.Validation.ScanOptionsValidator();
        var validation = validator.Validate(options);
        if (!validation.IsValid)
        {
            Message = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        IsScanning = true;
        ScanResult = null;
        CleanupResult = null;
        Candidates.Clear();
        Message = null;

        try
        {
            var ct = _scanCts.Token;
            var result = await Task.Run(
                () => _scanner.Scan(options, OnScanProgress, ct),
                ct);

            ScanResult = result;
            foreach (var candidate in result.Candidates)
            {
                var row = new CandidateRow(candidate);
                row.PropertyChanged += OnCandidatePropertyChanged;
                Candidates.Add(row);
            }

            Message = result.Candidates.Count == 0
                ? "Не знайдено кандидатів на очищення."
                : $"Проскановано {result.Summary.ScannedFiles:N0} файлів, знайдено кандидатів: {result.Candidates.Count}.";
        }
        catch (OperationCanceledException)
        {
            Message = "Сканування скасовано.";
        }
        catch (Exception ex)
        {
            Message = $"Помилка сканування: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanProgress = null;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    private void OnScanProgress(ScanProgress progress)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ScanProgress = progress;
            return;
        }

        dispatcher.Invoke(() => ScanProgress = progress);
    }

    private void OnCleanupProgress(CleanupProgress progress)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CleanupProgress = progress;
            return;
        }

        dispatcher.Invoke(() => CleanupProgress = progress);
    }

    [RelayCommand]
    private async Task CleanAsync(string mode)
    {
        var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Item).ToList();
        if (selected.Count == 0)
        {
            Message = "Нічого не вибрано.";
            return;
        }

        var cleanMode = mode switch
        {
            "dry-run" => CleanMode.DryRun,
            "auto" => CleanMode.Auto,
            _ => CleanMode.Interactive,
        };

        if (cleanMode == CleanMode.Interactive && !MoveToRecycleBin)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Видалення без кошика",
                "Файли не будуть переміщені в кошик. Великі файли потраплять у карантин SafeDisk. Продовжити?",
                "Продовжити");
            if (!confirmed)
            {
                return;
            }
        }

        IsCleaning = true;
        Message = null;
        try
        {
            var options = new CleanupOptions
            {
                Mode = cleanMode,
                QuarantineRetentionDays = _settings.QuarantineRetentionDays,
                MoveToRecycleBin = MoveToRecycleBin,
                AutoThreshold = _settings.AutoThreshold,
            };

            var result = await _cleanup.RunAsync(selected, options, OnCleanupProgress);
            CleanupResult = result;

            Message = result.Mode == CleanMode.DryRun
                ? $"Dry-run: було б звільнено {HumanSize.Format(result.FreedBytes)}."
                : $"Оброблено {result.Processed}, звільнено {HumanSize.Format(result.FreedBytes)}.";

            await _eventBus.RaiseDataChangedAsync();
        }
        catch (Exception ex)
        {
            Message = $"Помилка очищення: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            CleanupProgress = null;
        }
    }

    [RelayCommand]
    private async Task PickFoldersAsync()
    {
        var folders = await _dialogs.PickFoldersAsync("Виберіть папки або диски для аналізу");
        if (folders is null || folders.Length == 0)
        {
            return;
        }

        var merged = new HashSet<string>(ParseRoots(CustomRoots), StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            merged.Add(folder);
        }

        CustomRoots = string.Join(", ", merged);
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleDrive(string letter)
    {
        var root = $"{letter}\\";
        var existing = ParseRoots(CustomRoots).ToList();
        var has = existing.Contains(root, StringComparer.OrdinalIgnoreCase);
        var next = has
            ? existing.Where(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase)).ToList()
            : existing.Concat([root]).ToList();
        CustomRoots = string.Join(", ", next);
        SaveSettings();
    }

    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        if (!await _dialogs.ConfirmAsync("Очищення кошика", "Це назавжди видалить усі файли з кошика. Продовжити?", "Очистити"))
        {
            return;
        }

        try
        {
            var ok = await Task.Run(Core.Windows.WindowsApi.EmptyRecycleBin);
            Message = ok ? "Кошик очищено." : "Не вдалося очистити кошик.";
            if (ok)
            {
                await _eventBus.RaiseDataChangedAsync();
            }
        }
        catch (Exception ex)
        {
            Message = $"Помилка: {ex.Message}";
        }
    }

    private static List<string> ParseRoots(string input) =>
        input.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
