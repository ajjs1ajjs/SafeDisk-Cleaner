using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using SafeDiskCleaner.App.Services;
using SafeDiskCleaner.Core.Abstractions;

namespace SafeDiskCleaner.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IAppPaths _paths;
    private readonly IUpdateService _update;
    private readonly PaletteHelper _paletteHelper = new();

    [ObservableProperty]
    private string _dataRoot;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private uint _quarantineRetentionDays;

    [ObservableProperty]
    private byte _autoThreshold;

    [ObservableProperty]
    private string _version = "0.0.0";

    [ObservableProperty]
    private string? _updateText;

    public SettingsViewModel(AppSettings settings, IAppPaths paths, IUpdateService update)
    {
        _settings = settings;
        _paths = paths;
        _update = update;

        _dataRoot = paths.DataRoot;
        _isDarkTheme = settings.IsDarkTheme;
        _quarantineRetentionDays = settings.QuarantineRetentionDays;
        _autoThreshold = settings.AutoThreshold;
        _version = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplyTheme(value);
        Save();
    }

    partial void OnQuarantineRetentionDaysChanged(uint value) => Save();
    partial void OnAutoThresholdChanged(byte value) => Save();

    private void Save()
    {
        _settings.IsDarkTheme = IsDarkTheme;
        _settings.QuarantineRetentionDays = QuarantineRetentionDays;
        _settings.AutoThreshold = AutoThreshold;
        _settings.Save();
    }

    private void ApplyTheme(bool dark)
    {
        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
        _paletteHelper.SetTheme(theme);
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
