using SafeDiskCleaner.Core.Localization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.ViewModels.Abstractions;
using SafeDiskCleaner.ViewModels.Services;

namespace SafeDiskCleaner.ViewModels;

public sealed class NavItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _title;

    public NavItem(string titleKey, string iconKind, object target)
    {
        _title = Loc.T(titleKey);
        IconKind = iconKind;
        Target = target;

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            _title = Loc.T(titleKey);
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title)));
        };
    }

    public string Title => _title;

    public string IconKind { get; }

    public object Target { get; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings;
    private readonly IUpdateInstaller _updater;
    private readonly INavigationService _nav;
    private readonly DashboardViewModel _dashboard;
    private readonly IUiTimer _updateTimer;
    private readonly IAppLifecycle _lifecycle;

    public MainViewModel(
        AppSettings settings,
        IAppEventBus eventBus,
        IUpdateInstaller updater,
        IUiTimer updateTimer,
        IAppLifecycle lifecycle,
        INavigationService nav,
        ScanViewModel scan,
        DuplicatesViewModel duplicates,
        QuarantineViewModel quarantine,
        AuditViewModel audit,
        AppsViewModel apps,
        SettingsViewModel settingsVm,
        DashboardViewModel dashboard)
    {
        _settings = settings;
        _updater = updater;
        _updateTimer = updateTimer;
        _lifecycle = lifecycle;
        _nav = nav;
        _dashboard = dashboard;
        Scan = scan;
        Duplicates = duplicates;
        Quarantine = quarantine;
        Audit = audit;
        Apps = apps;
        Settings = settingsVm;

        NavItems =
        [
            new NavItem("Nav.Dashboard", "ViewDashboardOutline", dashboard),
            new NavItem("Nav.Scan", "MagnifyScan", scan),
            new NavItem("Nav.Duplicates", "ContentCopy", duplicates),
            new NavItem("Nav.Quarantine", "ShieldLock", quarantine),
            new NavItem("Nav.AuditLog", "TextBoxSearch", audit),
            new NavItem("Nav.Settings", "CogOutline", settingsVm),
        ];

        _nav.NavigateRequested += OnNavigateRequested;

        SelectedNavItem = NavItems[0];

        eventBus.DataChanged += OnDataChangedAsync;

        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
    }

    public DashboardViewModel Dashboard => _dashboard;

    private void OnNavigateRequested(object target)
    {
        var navItem = NavItems.FirstOrDefault(n => ReferenceEquals(n.Target, target));
        if (navItem is not null)
        {
            SelectedNavItem = navItem;
        }
    }

    public ScanViewModel Scan { get; }
    public DuplicatesViewModel Duplicates { get; }
    public QuarantineViewModel Quarantine { get; }
    public AuditViewModel Audit { get; }
    public AppsViewModel Apps { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Target;
        if (ReferenceEquals(value.Target, _dashboard))
        {
            _ = _dashboard.RefreshAsync();
        }
    }

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private UpdateInfo? _updateInfo;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string? _updateStatus;

    public bool IsUpdateBannerVisible => UpdateInfo is { Available: true };

    partial void OnUpdateInfoChanged(UpdateInfo? value) => OnPropertyChanged(nameof(IsUpdateBannerVisible));

    public string LatestVersionText => UpdateInfo?.LatestVersion ?? string.Empty;

    private async Task OnDataChangedAsync()
    {
        await Quarantine.RefreshAsync();
        await Audit.RefreshAsync();
    }

    public async Task InitializeAsync()
    {
        _settings.Load();
        LocalizationService.Instance.SetLanguage(_settings.Language);
        Scan.LoadSavedOptions();
        Settings.ApplyLoadedTheme();
        await Quarantine.RefreshAsync();
        await Audit.RefreshAsync();
        await _dashboard.RefreshAsync();

        _updateTimer.Start(UpdateCheckInterval);
        await CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            UpdateInfo = await _updater.CheckAsync();
            OnPropertyChanged(nameof(LatestVersionText));
        }
        catch (Exception ex)
        {
            // update check must never break the app
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdc-update-error.log"),
                    ex.ToString());
            }
            catch
            {
                // ignore
            }
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (UpdateInfo is not { Available: true })
        {
            return;
        }

        var asset = _updater.SelectAsset(UpdateInfo);
        if (asset is null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(UpdateInfo.DownloadUrl) { UseShellExecute = true });
            }
            catch
            {
                // cannot open browser
            }

            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        UpdateStatus = Loc.T("Update.Checking");
        try
        {
            var destination = Path.Combine(
                Path.GetTempPath(),
                $"SafeDisk-{UpdateInfo.LatestVersion}-{Path.GetFileName(asset.Name)}");
            var progress = new Progress<double>(p => DownloadProgress = p);

            await _updater.DownloadAsync(asset, destination, progress);

            // Integrity: when the release ships a "<asset>.sha256" companion,
            // the download must match before anything is executed.
            if (_updater.SelectChecksumAsset(UpdateInfo) is { } checksumAsset)
            {
                var checksum = await _updater.DownloadTextAsync(checksumAsset);
                _updater.VerifySha256(destination, checksum);
            }

            UpdateStatus = Loc.T("Update.Installing");
            await Task.Delay(300);

            _updater.LaunchInstaller(destination);

            // The current executable is being replaced — close the app.
            await _lifecycle.ShutdownAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus = Loc.F("Common.Error", ex.Message);
        }
        finally
        {
            IsDownloading = false;
        }
    }
}
