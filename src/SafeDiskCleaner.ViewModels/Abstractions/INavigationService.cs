namespace SafeDiskCleaner.ViewModels.Abstractions;

/// <summary>Decouples page navigation from view models (MVVM-friendly).</summary>
public interface INavigationService
{
    event Action<object>? NavigateRequested;

    void NavigateTo(object target);
}
