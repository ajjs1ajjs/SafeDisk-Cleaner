using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;

namespace SafeDiskCleaner.App.ViewModels;

public sealed class NavItem
{
    public required string Title { get; init; }
    public required string IconKind { get; init; }
    public required object Target { get; init; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public MainViewModel(
        AppSettings settings,
        IAppEventBus eventBus,
        ScanViewModel scan,
        DuplicatesViewModel duplicates,
        QuarantineViewModel quarantine,
        AuditViewModel audit,
        SettingsViewModel settingsVm)
    {
        _settings = settings;
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

        _selectedNavItem = NavItems[0];

        eventBus.DataChanged += OnDataChangedAsync;
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
    }
}
