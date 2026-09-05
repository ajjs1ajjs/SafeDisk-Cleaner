using SafeDiskCleaner.ViewModels.Abstractions;

namespace SafeDiskCleaner.App.Services;

public sealed class NavigationService : INavigationService
{
    public event Action<object>? NavigateRequested;

    public void NavigateTo(object target) => NavigateRequested?.Invoke(target);
}