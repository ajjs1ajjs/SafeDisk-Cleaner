using System.IO;
using SafeDiskCleaner.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeDiskCleaner.ViewModels.Abstractions;
using SafeDiskCleaner.ViewModels.Services;
using SafeDiskCleaner.Core.Abstractions;

namespace SafeDiskCleaner.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IAppPaths _paths;
    private readonly IUpdateService _update;
    private readonly IThemeService _themeService;
    private readonly IScheduleService _schedule;

    public IReadOnlyList<string> AccentOptions { get; } = ["Cyan", "Purple", "Green", "Amber"];

    public IReadOnlyList<LanguageOption> FrequencyOptions { get; } =
    [
        new LanguageOption("daily", Loc.T("Settings.ScheduleDaily")),
        new LanguageOption("weekly", Loc.T("Settings.ScheduleWeekly")),
    ];

    /// <summary>Combo-box friendly proxy over <see cref="ScheduleFrequency"/>.</summary>
    public LanguageOption SelectedFrequency
    {
        get => FrequencyOptions.First(o => o.Code == ScheduleMode);
        set
        {
            if (value is not null && value.Code != ScheduleMode)
            {
                ScheduleMode = value.Code;
            }
        }
    }

    /// <summary>Language codes offered in the UI, aligned with the localization catalogs.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new LanguageOption("uk", "Українська"),
        new LanguageOption("en", "English"),
        new LanguageOption("pl", "Polski"),
    ];

    /// <summary>Combo-box friendly proxy over <see cref="Language"/>.</summary>
    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.First(o => o.Code == Language);
        set
        {
            if (value is not null && value.Code != Language)
            {
                Language = value.Code;
            }
        }
    }

    [ObservableProperty]
    private string _dataRoot;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _accentPreset;

    [ObservableProperty]
    private string _language = LocalizationService.DefaultLanguage;

    [ObservableProperty]
    private uint _quarantineRetentionDays;

    [ObservableProperty]
    private byte _autoThreshold;

    [ObservableProperty]
    private string _version = "0.0.0";

    [ObservableProperty]
    private string? _updateText;

    /// <summary>One exclusion pattern per line (paths or * / ? wildcards).</summary>
    [ObservableProperty]
    private string _exclusionsText = string.Empty;

    [ObservableProperty]
    private bool _scheduleEnabled;

    [ObservableProperty]
    private string _scheduleMode = "daily";

    [ObservableProperty]
    private string _scheduleTime = "03:00";

    [ObservableProperty]
    private string? _scheduleStatus;

    public SettingsViewModel(
        AppSettings settings,
        IAppPaths paths,
        IUpdateService update,
        IThemeService themeService,
        IScheduleService schedule)
    {
        _settings = settings;
        _paths = paths;
        _update = update;
        _themeService = themeService;
        _schedule = schedule;

        _dataRoot = paths.DataRoot;
        _isDarkTheme = settings.IsDarkTheme;
        _accentPreset = settings.AccentPreset;
        _language = settings.Language;
        _quarantineRetentionDays = settings.QuarantineRetentionDays;
        _autoThreshold = settings.AutoThreshold;
        _exclusionsText = string.Join(Environment.NewLine, settings.Exclusions);
        _scheduleEnabled = settings.ScheduleEnabled;
        _scheduleMode = settings.ScheduleFrequency;
        _scheduleTime = settings.ScheduleTime;
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

    partial void OnLanguageChanged(string value) => Save();

    partial void OnQuarantineRetentionDaysChanged(uint value) => Save();
    partial void OnAutoThresholdChanged(byte value) => Save();

    partial void OnExclusionsTextChanged(string value) => Save();

    partial void OnScheduleEnabledChanged(bool value) => _ = ApplyScheduleAsync();

    partial void OnScheduleModeChanged(string value) => _ = ApplyScheduleAsync();

    partial void OnScheduleTimeChanged(string value) => _ = ApplyScheduleAsync();

    private void Save()
    {
        _settings.IsDarkTheme = IsDarkTheme;
        _settings.AccentPreset = AccentPreset;
        _settings.Language = Language;
        _settings.QuarantineRetentionDays = QuarantineRetentionDays;
        _settings.AutoThreshold = AutoThreshold;
        _settings.Exclusions = ExclusionsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.ScheduleEnabled = ScheduleEnabled;
        _settings.ScheduleFrequency = ScheduleMode;
        _settings.ScheduleTime = ScheduleTime;
        _settings.Save();
        LocalizationService.Instance.SetLanguage(Language);
    }

    /// <summary>Creates/removes the OS scheduled task to match the current settings.</summary>
    [RelayCommand]
    private async Task ApplyScheduleAsync()
    {
        try
        {
            if (!_schedule.IsSupported)
            {
                ScheduleStatus = Loc.T("Settings.ScheduleUnsupported");
                return;
            }

            if (!ScheduleEnabled)
            {
                await _schedule.RemoveAsync();
                ScheduleStatus = Loc.T("Settings.ScheduleRemoved");
                return;
            }

            var cli = FindCliExecutable();
            if (cli is null)
            {
                ScheduleStatus = Loc.T("Settings.ScheduleMissingCli");
                return;
            }

            await _schedule.ApplyAsync(new ScheduleOptions
            {
                ExecutablePath = cli,
                Arguments = "clean --auto",
                TimeOfDay = string.IsNullOrWhiteSpace(ScheduleTime) ? "03:00" : ScheduleTime.Trim(),
                Frequency = ScheduleMode.Equals("weekly", StringComparison.OrdinalIgnoreCase)
                    ? ScheduleFrequency.Weekly
                    : ScheduleFrequency.Daily,
            });

            ScheduleStatus = Loc.T("Settings.ScheduleApplied");
        }
        catch (Exception ex)
        {
            ScheduleStatus = Loc.F("Settings.ScheduleError", ex.Message);
        }
    }

    private static string? FindCliExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "SafeDiskCleaner.Cli.exe" : "SafeDiskCleaner.Cli";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(path) ? path : null;
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        var info = await _update.CheckAsync();
        UpdateText = info.Available
            ? Loc.F("Settings.UpdateAvailable", info.LatestVersion)
            : Loc.F("Settings.UpToDate", info.CurrentVersion);
    }
}
