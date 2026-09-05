using System.Globalization;

namespace SafeDiskCleaner.Core.Localization;

/// <summary>
/// In-code catalog localization service (no satellite assemblies, identical
/// behavior under WPF and Avalonia). Missing keys fall back to English, then
/// to the key itself so a forgotten translation never crashes the UI.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguage = "uk";

    public static LocalizationService Instance { get; } = new();

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs;
    private IReadOnlyDictionary<string, string> _current;

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<string> SupportedLanguages { get; } = ["uk", "en", "pl"];

    public string Language { get; private set; }

    /// <summary>Public to allow tests and alternative hosts; UI code should prefer <see cref="Instance"/>.</summary>
    public LocalizationService()
    {
        _catalogs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["uk"] = Uk.Map,
            ["en"] = En.Map,
            ["pl"] = Pl.Map,
        };
        Language = DefaultLanguage;
        _current = _catalogs[DefaultLanguage];
    }

    public void SetLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            !_catalogs.TryGetValue(language, out var catalog))
        {
            language = DefaultLanguage;
            catalog = _catalogs[DefaultLanguage];
        }

        if (string.Equals(Language, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Language = language.ToLowerInvariant();
        _current = catalog;

        try
        {
            var culture = new CultureInfo(Language switch
            {
                "uk" => "uk-UA",
                "pl" => "pl-PL",
                _ => "en-US",
            });
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // keep current culture — the string catalog is what matters here
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key]
    {
        get
        {
            if (_current.TryGetValue(key, out var value))
            {
                return value;
            }

            return En.Map.TryGetValue(key, out var fallback)
                ? fallback
                : key;
        }
    }

    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, this[key], args);
}

/// <summary>
/// Convenience accessors for ViewModels: <c>Loc.T("Scan.Start")</c>,
/// <c>Loc.F("Common.Error", ex.Message)</c>.
/// </summary>
public static class Loc
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string F(string key, params object?[] args) => LocalizationService.Instance.Format(key, args);
}
