using Microsoft.Extensions.DependencyInjection;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.App.Views;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Scanning;
using SafeDiskCleaner.Core.Windows;
using SafeDiskCleaner.ViewModels;
using SafeDiskCleaner.ViewModels.Abstractions;
using SafeDiskCleaner.ViewModels.Services;

namespace SafeDiskCleaner.App;

public static class AppServices
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<AppSettings>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAppEventBus, AppEventBus>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IUpdateInstaller, AutoUpdater>();
        services.AddSingleton<SafeDiskCleaner.ViewModels.Abstractions.IDispatcher>(WpfDispatcher.Instance);
        services.AddSingleton<IUiTimer, WpfUiTimer>();
        services.AddSingleton<SafeDiskCleaner.ViewModels.Abstractions.IAppLifecycle>(WpfAppLifecycle.Instance);

        services.AddSingleton<SignatureInspector>();
        services.AddSingleton<SafetyValidator>();
        services.AddSingleton(sp => SafeDiskCleaner.Core.Rules.ScanRootsCatalog.LoadOrDefault(
            System.IO.Path.Combine(sp.GetRequiredService<SafeDiskCleaner.Core.Abstractions.IAppPaths>().DataRoot, "rules.json")));
        services.AddSingleton<SafeDiskCleaner.Core.Abstractions.IScheduleService, SafeDiskCleaner.Core.Platform.ScheduleService>();
        services.AddSingleton<Scanner>();
        services.AddSingleton<CleanupEngine>();

        services.AddSingleton<AppsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<QuarantineViewModel>();
        services.AddSingleton<DuplicatesViewModel>();
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();

        return services;
    }
}
