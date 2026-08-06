using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _updateTimer;

    public MainViewModel(
        AppSettings settings,
        IAppEventBus eventBus,
        AutoUpdater updater,
        ScanViewModel scan,
        DuplicatesViewModel duplicates,
        QuarantineViewModel quarantine,
        AuditViewModel audit,
        SettingsViewModel settingsVm)
    {
        _settings = settings;
        _updater = updater;
        Scan = scan;
        Duplicates = duplicates;
        Quarantine = quarantine;
        Audit = audit;
        Settings = settingsVm;

        NavItems =
        [
            new NavItem { Title = "Сканування", IconKind = "MagnifyScan", Target = scan },
            new NavItem { Title = "Дублікати", IconKind = "ContentCopy", Target = duplicates },
            new NavItem { Title = "Карантин", IconKind = "ShieldLock", Target = quarantine },
            new NavItem { Title = "Audit Log", IconKind = "TextBoxSearch", Target = audit },
            new NavItem { Title = "Налаштування", IconKind = "CogOutline", Target = settingsVm },
        ];

        SelectedNavItem = NavItems[0];

        eventBus.DataChanged += OnDataChangedAsync;

        _updateTimer = new DispatcherTimer { Interval = UpdateCheckInterval };
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
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
        if (value is not null)
        {
            CurrentPage = value.Target;
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
        await Quarantine.RefreshAsync();
        await Audit.RefreshAsync();

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
        catch
        {
            // update check must never break the app
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

            // The current executable is being replaced / reinstalled — close the app.
            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
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
