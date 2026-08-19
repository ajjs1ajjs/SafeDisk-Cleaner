using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.App.ViewModels;

public sealed class NavItem
{
    public required string Title { get; init; }
    public required string IconKind { get; init; }
    public required object Target { get; init; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings;
    private readonly AutoUpdater _updater;
    private readonly INavigationService _nav;
    private readonly DashboardViewModel _dashboard;
    private readonly DispatcherTimer _updateTimer;

    public MainViewModel(
        AppSettings settings,
        IAppEventBus eventBus,
        AutoUpdater updater,
        INavigationService nav,
        ScanViewModel scan,
        DuplicatesViewModel duplicates,
        QuarantineViewModel quarantine,
        AuditViewModel audit,
        SettingsViewModel settingsVm,
        DashboardViewModel dashboard)
    {
        _settings = settings;
        _updater = updater;
        _nav = nav;
        _dashboard = dashboard;
        Scan = scan;
        Duplicates = duplicates;
        Quarantine = quarantine;
        Audit = audit;
        Settings = settingsVm;

        NavItems =
        [
            new NavItem { Title = "Огляд", IconKind = "ViewDashboardOutline", Target = dashboard },
            new NavItem { Title = "Сканування", IconKind = "MagnifyScan", Target = scan },
            new NavItem { Title = "Дублікати", IconKind = "ContentCopy", Target = duplicates },
            new NavItem { Title = "Карантин", IconKind = "ShieldLock", Target = quarantine },
            new NavItem { Title = "Audit Log", IconKind = "TextBoxSearch", Target = audit },
            new NavItem { Title = "Налаштування", IconKind = "CogOutline", Target = settingsVm },
        ];

        _nav.NavigateRequested += OnNavigateRequested;

        SelectedNavItem = NavItems[0];

        eventBus.DataChanged += OnDataChangedAsync;

        _updateTimer = new DispatcherTimer { Interval = UpdateCheckInterval };
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
        Scan.LoadSavedOptions();
        Settings.ApplyLoadedTheme();
        await Quarantine.RefreshAsync();
        await Audit.RefreshAsync();
        await _dashboard.RefreshAsync();

        _updateTimer.Start();
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
        UpdateStatus = "Завантаження...";
        try
        {
            var destination = Path.Combine(
                Path.GetTempPath(),
                $"SafeDisk-{UpdateInfo.LatestVersion}-{Path.GetFileName(asset.Name)}");
            var progress = new Progress<double>(p => DownloadProgress = p);

            await _updater.DownloadAsync(asset, destination, progress);

            UpdateStatus = "Встановлення...";
            await Task.Delay(300);

            _updater.LaunchInstaller(destination);

            // The current executable is being replaced — close the app.
            await Dispatcher.UIThread.InvokeAsync(AppRuntime.RequestShutdown);
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Помилка: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }
}