using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Abstractions;

namespace SafeDiskCleaner.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IAppPaths _paths;
    private readonly IUpdateService _update;
    private readonly ThemeService _themeService;

    public IReadOnlyList<string> AccentOptions { get; } = ["Cyan", "Purple", "Green", "Amber"];

    [ObservableProperty]
    private string _dataRoot;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _accentPreset;

    [ObservableProperty]
    private uint _quarantineRetentionDays;

    [ObservableProperty]
    private byte _autoThreshold;

    [ObservableProperty]
    private string _version = "0.0.0";

    [ObservableProperty]
    private string? _updateText;

    public SettingsViewModel(AppSettings settings, IAppPaths paths, IUpdateService update, ThemeService themeService)
    {
        _settings = settings;
        _paths = paths;
        _update = update;
        _themeService = themeService;

        _dataRoot = paths.DataRoot;
        _isDarkTheme = settings.IsDarkTheme;
        _accentPreset = settings.AccentPreset;
        _quarantineRetentionDays = settings.QuarantineRetentionDays;
        _autoThreshold = settings.AutoThreshold;
        _version = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>Applies the saved theme once after settings are loaded from disk.</summary>
    public void ApplyLoadedTheme() => _themeService.Apply(IsDarkTheme, AccentPreset);

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeService.Apply(value, AccentPreset);
        Save();
    }

    partial void OnAccentPresetChanged(string value)
    {
        _themeService.Apply(IsDarkTheme, value);
        Save();
    }

    partial void OnQuarantineRetentionDaysChanged(uint value) => Save();
    partial void OnAutoThresholdChanged(byte value) => Save();

    private void Save()
    {
        _settings.IsDarkTheme = IsDarkTheme;
        _settings.AccentPreset = AccentPreset;
        _settings.QuarantineRetentionDays = QuarantineRetentionDays;
        _settings.AutoThreshold = AutoThreshold;
        _settings.Save();
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        var info = await _update.CheckAsync();
        UpdateText = info.Available
            ? $"Доступна версія {info.LatestVersion}"
            : $"Встановлена остання версія ({info.CurrentVersion})";
    }
}
