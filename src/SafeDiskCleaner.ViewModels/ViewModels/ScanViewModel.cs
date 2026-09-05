using DriveInfo = SafeDiskCleaner.Core.Models.DriveInfo;
using SafeDiskCleaner.Core.Localization;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Platform;
using SafeDiskCleaner.Core.Scanning;
using SafeDiskCleaner.Core.Utils;
using SafeDiskCleaner.ViewModels.Abstractions;
using SafeDiskCleaner.ViewModels.Services;

namespace SafeDiskCleaner.ViewModels;

public sealed partial class ScanViewModel : ObservableObject
{
    private readonly Scanner _scanner;
    private readonly CleanupEngine _cleanup;
    private readonly IDialogService _dialogs;
    private readonly IAppEventBus _eventBus;
    private readonly AppSettings _settings;
    private readonly IDispatcher _dispatcher;
    private readonly IRecycleBin _recycleBin;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _cleanupCts;

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

    public ObservableCollection<CandidateRow> FilteredCandidates { get; } = [];

    public IReadOnlyList<string> CategoryFilterOptions { get; } =
        new[] { Loc.T("Filter.AllCategories") }
            .Concat(Enum.GetValues<Category>().Select(c => c.Label()))
            .ToArray();

    public IReadOnlyList<string> RiskFilterOptions { get; } = [Loc.T("Filter.AllRisks"), "Safe", "Medium", "Advanced"];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _categoryFilter = Loc.T("Filter.AllCategories");

    [ObservableProperty]
    private string _riskFilter = Loc.T("Filter.AllRisks");

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnCategoryFilterChanged(string value) => ApplyFilters();
    partial void OnRiskFilterChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        FilteredCandidates.Clear();
        foreach (var row in Candidates)
        {
            if (!string.IsNullOrWhiteSpace(SearchText) &&
                !row.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (CategoryFilter is not null && CategoryFilter != Loc.T("Filter.AllCategories") &&
                row.CategoryLabel != CategoryFilter)
            {
                continue;
            }

            if (RiskFilter is not null && RiskFilter != Loc.T("Filter.AllRisks") &&
                row.RiskLevel.Label() != RiskFilter)
            {
                continue;
            }

            FilteredCandidates.Add(row);
        }

        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(FilteredSize));
        NotifySelectionChanged();
    }

    public int FilteredCount => FilteredCandidates.Count;
    public long FilteredSize => FilteredCandidates.Where(c => c.IsSelected).Sum(c => c.Size);

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        CategoryFilter = Loc.T("Filter.AllCategories");
        RiskFilter = Loc.T("Filter.AllRisks");
        ApplyFilters();
    }

    [RelayCommand]
    private void SelectOnlySafe()
    {
        _batchUpdating = true;
        try
        {
            foreach (var row in Candidates)
            {
                row.IsSelected = row.RiskLevel == RiskLevel.Safe && row.IsSelectable;
            }
        }
        finally
        {
            _batchUpdating = false;
        }

        ApplyFilters();
        NotifySelectionChanged();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = await _dialogs.PickSaveFileAsync(
            Loc.T("Export.SaveReport"),
            $"SafeDisk-report-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json");

        if (path is null)
        {
            return;
        }

        try
        {
            var rows = FilteredCandidates.Count > 0 ? FilteredCandidates : Candidates;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    rows.Select(r => new
                    {
                        r.Path,
                        Category = r.CategoryLabel,
                        Risk = r.RiskLevel.Label(),
                        r.Size,
                        r.Confidence,
                        r.Recommendation,
                        r.Reason,
                        LastAccessDays = r.LastAccessDays,
                    }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(path, json);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("Path,Category,Risk,SizeBytes,Confidence,Recommendation,Reason");
                foreach (var r in rows)
                {
                    sb.AppendLine($"\"{r.Path}\",{r.CategoryLabel},{r.RiskLevel.Label()},{r.Size},{r.Confidence},\"{r.Recommendation}\",\"{r.Reason}\"");
                }

                await System.IO.File.WriteAllTextAsync(path, sb.ToString());
            }

            Message = Loc.F("Export.Saved", path);
        }
        catch (Exception ex)
        {
            Message = Loc.F("Export.Error", ex.Message);
        }
    }

    public long SelectedSize => Candidates.Where(c => c.IsSelected).Sum(c => c.Size);
    public int SelectedCount => Candidates.Count(c => c.IsSelected);
    /// <summary>One rectangle of the results treemap (category-sized).</summary>
    public sealed record TreemapTileVm(string Label, string SizeText, double X, double Y, double Width, double Height);

    public ObservableCollection<TreemapTileVm> TreemapTiles { get; } = [];

    [ObservableProperty]
    private bool _showTreemap;

    partial void OnShowTreemapChanged(bool value)
    {
        if (value)
        {
            BuildTreemap();
        }
    }

    private void BuildTreemap()
    {
        TreemapTiles.Clear();
        if (ScanResult is null || ScanResult.Candidates.Count == 0)
        {
            return;
        }

        const double canvasWidth = 800;
        const double canvasHeight = 320;

        var groups = ScanResult.Candidates
            .GroupBy(c => c.Category.Label())
            .Select(g => (Label: g.Key, Size: (double)g.Sum(c => c.Size)))
            .Where(g => g.Size > 0)
            .ToList();

        var inputs = groups.Select(g => new Core.Utils.TreemapInput(g.Label, g.Size)).ToList();
        foreach (var tile in Core.Utils.SquarifiedTreemap.Layout(inputs, canvasWidth, canvasHeight))
        {
            var size = groups.First(g => g.Label == tile.Id).Size;
            TreemapTiles.Add(new TreemapTileVm(tile.Id, HumanSize.Format((long)size), tile.X, tile.Y, tile.Width, tile.Height));
        }
    }

    public ScanViewModel(
        Scanner scanner,
        CleanupEngine cleanup,
        IDialogService dialogs,
        IAppEventBus eventBus,
        AppSettings settings,
        Core.Abstractions.IAppPaths paths,
        IDispatcher dispatcher,
        IDriveService drives,
        IRecycleBin recycleBin)
    {
        _scanner = scanner;
        _cleanup = cleanup;
        _dialogs = dialogs;
        _eventBus = eventBus;
        _settings = settings;
        _dispatcher = dispatcher;
        _recycleBin = recycleBin;

        _customRoots = settings.CustomRoots;
        _includeMedium = settings.IncludeMedium;
        _includeAdvanced = settings.IncludeAdvanced;
        _minConfidence = settings.MinConfidence;
        _recencyDays = settings.RecencyDays;
        _moveToRecycleBin = settings.MoveToRecycleBin;
        DataRoot = paths.DataRoot;

        Drives = new ObservableCollection<DriveInfo>(drives.ListDrives());
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
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(FilteredSize));
    }

    public void ToggleCandidateSelection(CandidateRow row) => NotifySelectionChanged();

    private bool _batchUpdating;

    private void OnCandidatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CandidateRow.IsSelected) && !_batchUpdating)
        {
            NotifySelectionChanged();
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
            Exclusions = _settings.Exclusions,
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
            var result = await _scanner.ScanAsync(options, OnScanProgress, ct);

            ScanResult = result;
            foreach (var candidate in result.Candidates)
            {
                var row = new CandidateRow(candidate);
                row.PropertyChanged += OnCandidatePropertyChanged;
                Candidates.Add(row);
            }

            ApplyFilters();

                        if (ShowTreemap)
            {
                BuildTreemap();
            }

Message = result.Candidates.Count == 0
                ? Loc.T("Scan.NoCandidates")
                : Loc.F("Scan.Completed", result.Summary.ScannedFiles, result.Candidates.Count);
        }
        catch (OperationCanceledException)
        {
            Message = Loc.T("Scan.Cancelled");
        }
        catch (Exception ex)
        {
            Message = Loc.F("Scan.Error", ex.Message);
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
        if (_dispatcher.CheckAccess())
        {
            ScanProgress = progress;
            return;
        }

        _dispatcher.Invoke(() => ScanProgress = progress);
    }

    private void OnCleanupProgress(CleanupProgress progress)
    {
        if (_dispatcher.CheckAccess())
        {
            CleanupProgress = progress;
            return;
        }

        _dispatcher.Invoke(() => CleanupProgress = progress);
    }

    [RelayCommand]
    private async Task CleanAsync(string mode)
    {
        var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Item).ToList();
        if (selected.Count == 0)
        {
            Message = Loc.T("Common.NothingSelected");
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
                Loc.T("Scan.DeleteWithoutBinTitle"),
                Loc.T("Scan.DeleteWithoutBinMsg"),
                Loc.T("Common.Continue"));
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

            _cleanupCts?.Cancel();
            _cleanupCts = new CancellationTokenSource();

            var result = await Task.Run(
                () => _cleanup.RunAsync(selected, options, OnCleanupProgress, _cleanupCts.Token),
                _cleanupCts.Token);
            CleanupResult = result;

            Message = result.Mode == CleanMode.DryRun
                ? Loc.F("Common.DryRunFreed", HumanSize.Format(result.FreedBytes))
                : Loc.F("Common.ProcessedFreed", result.Processed, HumanSize.Format(result.FreedBytes));

            await _eventBus.RaiseDataChangedAsync();
        }
        catch (Exception ex)
        {
            Message = Loc.F("Cleanup.Error", ex.Message);
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
        var folders = await _dialogs.PickFoldersAsync(Loc.T("Scan.PickFolders"));
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
        var root = DriveInfoExtensions.DriveRoot(letter);
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
        if (!await _dialogs.ConfirmAsync(Loc.T("RecycleBin.CleanConfirmTitle"), Loc.T("RecycleBin.CleanConfirmMsg"), Loc.T("Common.Clear")))
        {
            return;
        }

        try
        {
            var ok = await Task.Run(_recycleBin.Empty);
            Message = ok ? Loc.T("RecycleBin.Cleaned") : Loc.T("RecycleBin.CleanFailed");
            if (ok)
            {
                await _eventBus.RaiseDataChangedAsync();
            }
        }
        catch (Exception ex)
        {
            Message = Loc.F("Common.Error", ex.Message);
        }
    }

    private static List<string> ParseRoots(string input) =>
        input.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
