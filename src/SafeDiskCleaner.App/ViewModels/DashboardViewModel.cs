using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.App.ViewModels;

public sealed record DriveUsage(
    string Letter,
    string Kind,
    string UsedText,
    string FreeText,
    string TotalText,
    double UsedPercent);

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IAuditService _audit;
    private readonly IQuarantineService _quarantine;
    private readonly INavigationService _nav;
    private readonly ScanViewModel _scan;
    private readonly DuplicatesViewModel _duplicates;
    private readonly QuarantineViewModel _quarantineVm;
    private readonly AuditViewModel _auditVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly IAppEventBus _eventBus;

    public ObservableCollection<DriveUsage> Drives { get; } = [];

    [ObservableProperty]
    private string _totalFreed = "0 B";

    [ObservableProperty]
    private int _totalCleanedEntries;

    [ObservableProperty]
    private string _quarantineSize = "0 B";

    [ObservableProperty]
    private int _quarantineCount;

    [ObservableProperty]
    private string _lastScanPotential = "—";

    [ObservableProperty]
    private int _lastScanCandidates;

    [ObservableProperty]
    private bool _isRefreshing;

    public DashboardViewModel(
        IAuditService audit,
        IQuarantineService quarantine,
        INavigationService nav,
        ScanViewModel scan,
        DuplicatesViewModel duplicates,
        QuarantineViewModel quarantineVm,
        AuditViewModel auditVm,
        SettingsViewModel settingsVm,
        IAppEventBus eventBus)
    {
        _audit = audit;
        _quarantine = quarantine;
        _nav = nav;
        _scan = scan;
        _duplicates = duplicates;
        _quarantineVm = quarantineVm;
        _auditVm = auditVm;
        _settingsVm = settingsVm;
        _eventBus = eventBus;

        _eventBus.DataChanged += async () => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            ReloadDrives();
            ReloadScanStats();
            await ReloadAuditStatsAsync();
            await ReloadQuarantineStatsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dashboard refresh failed: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ReloadDrives()
    {
        Drives.Clear();
        foreach (var d in Core.Windows.WindowsApi.ListDrives())
        {
            var percent = d.Total > 0
                ? Math.Clamp((double)d.Used / d.Total * 100.0, 0, 100)
                : 0;
            Drives.Add(new DriveUsage(
                d.Letter,
                d.Kind,
                HumanSize.Format((long)d.Used),
                HumanSize.Format((long)d.Free),
                HumanSize.Format((long)d.Total),
                percent));
        }
    }

    private void ReloadScanStats()
    {
        if (_scan.ScanResult is { } result)
        {
            LastScanPotential = HumanSize.Format(result.Summary.TotalPotential);
            LastScanCandidates = result.Candidates.Count;
        }
        else
        {
            LastScanPotential = "—";
            LastScanCandidates = 0;
        }
    }

    private async Task ReloadAuditStatsAsync()
    {
        var entries = await _audit.GetAllAsync();
        var freed = entries.Where(e => e.Success).Sum(e => e.Size);
        TotalFreed = HumanSize.Format(freed);
        TotalCleanedEntries = entries.Count(e => e.Success);
    }

    private async Task ReloadQuarantineStatsAsync()
    {
        var entries = await _quarantine.ListAsync();
        QuarantineCount = entries.Count;
        QuarantineSize = HumanSize.Format(entries.Sum(e => e.Size));
    }

    [RelayCommand]
    private void GoScan() => _nav.NavigateTo(_scan);

    [RelayCommand]
    private void GoDuplicates() => _nav.NavigateTo(_duplicates);

    [RelayCommand]
    private void GoQuarantine() => _nav.NavigateTo(_quarantineVm);

    [RelayCommand]
    private void GoAudit() => _nav.NavigateTo(_auditVm);

    [RelayCommand]
    private void GoSettings() => _nav.NavigateTo(_settingsVm);

    [RelayCommand]
    private void GoScanNow()
    {
        _nav.NavigateTo(_scan);
        if (!_scan.IsScanning)
        {
            _scan.ScanCommand.Execute(null);
        }
    }
}
