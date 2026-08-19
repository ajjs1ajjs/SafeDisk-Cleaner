using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.App.ViewModels;
using SafeDiskCleaner.App.Views;
using SafeDiskCleaner.Infrastructure;

namespace SafeDiskCleaner.App;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var errorLog = Path.Combine(Path.GetTempPath(), "sdc-app-error.log");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { File.WriteAllText(errorLog, e.ExceptionObject.ToString()); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try { File.WriteAllText(errorLog, e.Exception.ToString()); } catch { }
            e.SetObserved();
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.AddSafeDiskInfrastructure(ctx.Configuration);
                services.AddSafeDiskDatabase();
                services.AddAppServices();
            })
            .UseSerilogDefaults()
            .Build();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                _host?.Start();
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "sdc-startup-error.log"), ex.ToString()); } catch { }
            }

            var mainWindow = _host?.Services.GetRequiredService<MainWindow>();
            if (mainWindow is not null)
            {
                desktop.MainWindow = mainWindow;
                mainWindow.DataContext = _host!.Services.GetRequiredService<MainViewModel>();
                DialogService.MainWindow = mainWindow;

                var vm = (MainViewModel)mainWindow.DataContext;
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try { await vm.InitializeAsync(); } catch { }
                });
            }

            desktop.Exit += (_, _) =>
            {
                try
                {
                    if (_host is not null)
                    {
                        _host.StopAsync().GetAwaiter().GetResult();
                        _host.Dispose();
                    }
                }
                catch
                {
                    // best-effort shutdown
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Small static helpers bridging the WPF-era view models to Avalonia.</summary>
public static class AppRuntime
{
    public static void RequestShutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
    }
}