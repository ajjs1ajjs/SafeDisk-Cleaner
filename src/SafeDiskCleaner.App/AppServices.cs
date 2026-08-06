using Microsoft.Extensions.DependencyInjection;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.App.ViewModels;
using SafeDiskCleaner.App.Views;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Scanning;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.App;

public static class AppServices
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<AppSettings>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAppEventBus, AppEventBus>();
        services.AddSingleton<AutoUpdater>();

        services.AddSingleton<SignatureInspector>();
        services.AddSingleton<SafetyValidator>();
        services.AddSingleton<Scanner>();
        services.AddSingleton<CleanupEngine>();

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<QuarantineViewModel>();
        services.AddSingleton<DuplicatesViewModel>();
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();

        return services;
    }
}
