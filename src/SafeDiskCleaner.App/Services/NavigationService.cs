namespace SafeDiskCleaner.App.Services;

/// <summary>Decouples page navigation from view models (MVVM-friendly).</summary>
public interface INavigationService
{
    event Action<object>? NavigateRequested;
    void NavigateTo(object target);
}

public sealed class NavigationService : INavigationService
{
    public event Action<object>? NavigateRequested;

    public void NavigateTo(object target) => NavigateRequested?.Invoke(target);
}
