using System.Windows;
using System.Windows.Threading;

namespace SafeDiskCleaner.App.Services;

/// <summary>Bridges the shared ViewModels to the WPF dispatcher.</summary>
public sealed class WpfDispatcher : SafeDiskCleaner.ViewModels.Abstractions.IDispatcher
{
    public static readonly WpfDispatcher Instance = new();

    public bool CheckAccess()
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
        return dispatcher is null || dispatcher.CheckAccess();
    }

    public void Invoke(Action action) => Application.Current?.Dispatcher.Invoke(action);
}

/// <summary>Wraps the WPF DispatcherTimer for the shared <c>IUiTimer</c> abstraction.</summary>
public sealed class WpfUiTimer : DispatcherTimer, SafeDiskCleaner.ViewModels.Abstractions.IUiTimer
{
    event EventHandler? SafeDiskCleaner.ViewModels.Abstractions.IUiTimer.Tick
    {
        add => Tick += value;
        remove => Tick -= value;
    }

    void SafeDiskCleaner.ViewModels.Abstractions.IUiTimer.Start(TimeSpan interval)
    {
        Interval = interval;
        Start();
    }

    void SafeDiskCleaner.ViewModels.Abstractions.IUiTimer.Stop() => Stop();

    void IDisposable.Dispose() => Stop();
}

/// <summary>Shuts the WPF application down on the UI thread.</summary>
public sealed class WpfAppLifecycle : SafeDiskCleaner.ViewModels.Abstractions.IAppLifecycle
{
    public static readonly WpfAppLifecycle Instance = new();

    public Task ShutdownAsync()
    {
        var app = Application.Current;
        if (app is null)
        {
            return Task.CompletedTask;
        }

        return app.Dispatcher.InvokeAsync(app.Shutdown).Task;
    }
}
