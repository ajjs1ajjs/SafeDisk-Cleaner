using Avalonia.Threading;

namespace SafeDiskCleaner.App.Services;

/// <summary>Bridges the shared ViewModels to the Avalonia UI thread.</summary>
public sealed class AvaloniaDispatcher : SafeDiskCleaner.ViewModels.Abstractions.IDispatcher
{
    public static readonly AvaloniaDispatcher Instance = new();

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);
}

/// <summary>Wraps the Avalonia DispatcherTimer for the shared <c>IUiTimer</c> abstraction.</summary>
public sealed class AvaloniaUiTimer : DispatcherTimer, SafeDiskCleaner.ViewModels.Abstractions.IUiTimer
{
    event EventHandler? SafeDiskCleaner.ViewModels.Abstractions.IUiTimer.Tick
    {
        add => base.Tick += value;
        remove => base.Tick -= value;
    }

    void SafeDiskCleaner.ViewModels.Abstractions.IUiTimer.Start(TimeSpan interval)
    {
        Interval = interval;
        base.Start();
    }

    void SafeDiskCleaner.ViewModels.Abstractions.IUiTimer.Stop() => base.Stop();

    void IDisposable.Dispose()
    {
        // Avalonia's DispatcherTimer is not disposable; nothing to release.
    }
}

/// <summary>Shuts the Avalonia application down on the UI thread.</summary>
public sealed class AvaloniaAppLifecycle : SafeDiskCleaner.ViewModels.Abstractions.IAppLifecycle
{
    public static readonly AvaloniaAppLifecycle Instance = new();

    public async Task ShutdownAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(ShutdownCore);
            return;
        }

        ShutdownCore();
    }

    private static void ShutdownCore()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
