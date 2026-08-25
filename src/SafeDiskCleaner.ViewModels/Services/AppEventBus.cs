using SafeDiskCleaner.ViewModels.Abstractions;

namespace SafeDiskCleaner.ViewModels.Services;

/// <summary>
/// In-process event bus implementation shared by the WPF and Avalonia hosts.
/// </summary>
public sealed class AppEventBus : IAppEventBus
{
    public event Func<Task>? DataChanged;

    public async Task RaiseDataChangedAsync()
    {
        var handlers = DataChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }
}
