namespace SafeDiskCleaner.Core.Localization;

/// <summary>
/// Provides translated UI strings. Shared by the WPF and Avalonia hosts.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Currently active language code (e.g. "uk", "en", "pl").</summary>
    string Language { get; }

    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>Raised after the language changes; subscribers should refresh bound strings.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Switches the active language. Falls back to the default for unknown codes.</summary>
    void SetLanguage(string language);

    /// <summary>Returns the translation for <paramref name="key"/> or the key itself when missing.</summary>
    string this[string key] { get; }

    /// <summary>Formats the translation for <paramref name="key"/> with positional arguments.</summary>
    string Format(string key, params object?[] args);
}
