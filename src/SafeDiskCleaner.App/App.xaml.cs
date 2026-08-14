using System.IO;
using System.Windows;
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
    private readonly IHost _host;

    public App()
    {
        var errorLog = Path.Combine(Path.GetTempPath(), "sdc-app-error.log");
        DispatcherUnhandledException += (_, e) =>
        {
            try { File.WriteAllText(errorLog, e.Exception.ToString()); } catch { }
            e.Handled = true;
        };
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        finally
        {
            base.OnExit(e);
        }
    }
}
