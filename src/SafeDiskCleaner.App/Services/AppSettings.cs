using System.IO;
using System.Text.Json;
using SafeDiskCleaner.Core.Abstractions;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// User preferences persisted to <c>settings.json</c> under the data root.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAppPaths _paths;
    private string? _filePath;

    public bool IsDarkTheme { get; set; } = true;
    public string AccentPreset { get; set; } = "Cyan";
    public uint QuarantineRetentionDays { get; set; } = 14;
    public byte AutoThreshold { get; set; } = 95;
    public byte MinConfidence { get; set; } = 50;
    public uint RecencyDays { get; set; } = 3;
    public bool IncludeMedium { get; set; }
    public bool IncludeAdvanced { get; set; }
    public bool MoveToRecycleBin { get; set; } = true;
    public string CustomRoots { get; set; } = string.Empty;

    public AppSettings(IAppPaths paths)
    {
        _paths = paths;
    }

    public void Load()
    {
        try
        {
            var file = ResolveFile();
            if (!File.Exists(file))
            {
                return;
            }

            var json = File.ReadAllText(file);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return;
            }

            IsDarkTheme = loaded.IsDarkTheme;
            AccentPreset = loaded.AccentPreset;
            QuarantineRetentionDays = loaded.QuarantineRetentionDays;
            AutoThreshold = loaded.AutoThreshold;
            MinConfidence = loaded.MinConfidence;
            RecencyDays = loaded.RecencyDays;
            IncludeMedium = loaded.IncludeMedium;
            IncludeAdvanced = loaded.IncludeAdvanced;
            MoveToRecycleBin = loaded.MoveToRecycleBin;
            CustomRoots = loaded.CustomRoots;
        }
        catch
        {
            // corrupted settings file is ignored — defaults are used
        }
    }

    public void Save()
    {
        try
        {
            var file = ResolveFile();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // persistence is best-effort
        }
    }

    private string ResolveFile()
    {
        _filePath ??= Path.Combine(_paths.DataRoot, "settings.json");
        return _filePath;
    }
}
