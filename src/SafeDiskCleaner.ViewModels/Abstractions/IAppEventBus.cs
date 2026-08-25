namespace SafeDiskCleaner.ViewModels.Abstractions;

/// <summary>
/// Lightweight in-process event bus used to refresh shared state
/// (quarantine, audit log, drives) after a cleanup cycle completes.
/// </summary>
public interface IAppEventBus
{
    event Func<Task>? DataChanged;

    Task RaiseDataChangedAsync();
}
