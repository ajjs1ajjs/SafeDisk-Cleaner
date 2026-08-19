namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Lightweight in-process event bus used to refresh shared state
/// (quarantine, audit log, drives) after a cleanup cycle completes.
/// </summary>
public interface IAppEventBus
{
    event Func<Task>? DataChanged;
    Task RaiseDataChangedAsync();
}

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