using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeDiskCleaner.Core.Rules;

/// <summary>When a scan-root group becomes active.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RootTier
{
    Always,
    Medium,
    Advanced,
}

/// <summary>How <see cref="ScanRootGroup.Subdirectories"/> attach to the base directory.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubPathJoin
{
    /// <summary>String concatenation: base + sub (sub already contains separators).</summary>
    Append,

    /// <summary><see cref="Path.Combine"/> semantics.</summary>
    Combine,
}

/// <summary>A declarative group of scan roots sharing one base directory.</summary>
public sealed class ScanRootGroup
{
    /// <summary>Stable identifier used when merging user overrides.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Operating systems the group applies to: "windows", "linux", "macos". Empty = all.</summary>
    public string[] Os { get; set; } = [];

    /// <summary>Base directory token ($TEMP, $LOCALAPPDATA, $APPDATA, $PROFILE, $WINDIR, $SYSTEMDRIVE, $CACHE) or a literal path.</summary>
    public string Base { get; set; } = string.Empty;

    /// <summary>Relative sub-paths; an empty entry means the base directory itself.</summary>
    public string[] Subdirectories { get; set; } = [""];

    public RootTier Tier { get; set; } = RootTier.Always;

    public SubPathJoin Join { get; set; } = SubPathJoin.Append;
}

/// <summary>
/// Declarative catalog of default scan roots. Shipped as an embedded resource;
/// hosts may supply a JSON override file (groups are matched by <see cref="ScanRootGroup.Id"/>,
/// unknown ids are appended) so new categories can be added without recompiling.
/// </summary>
public sealed class ScanRootsCatalog
{
    private const string EmbeddedResourceName = "SafeDiskCleaner.Core.Rules.scan-roots.json";

    private static readonly Lazy<ScanRootsCatalog> EmbeddedLazy = new(LoadEmbedded);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public List<ScanRootGroup> Groups { get; set; } = [];

    public static ScanRootsCatalog Embedded => EmbeddedLazy.Value;

    /// <summary>
    /// Loads the embedded default catalog, then merges the JSON overrides from
    /// <paramref name="overridesPath"/> when the file exists. A malformed
    /// override file is ignored (defaults remain effective).
    /// </summary>
    public static ScanRootsCatalog LoadOrDefault(string? overridesPath)
    {
        var catalog = Embedded;
        if (!string.IsNullOrWhiteSpace(overridesPath) && File.Exists(overridesPath))
        {
            try
            {
                catalog = Merge(catalog, File.ReadAllText(overridesPath));
            }
            catch (JsonException)
            {
                // invalid override — fall back to defaults
            }
        }

        return catalog;
    }

    /// <summary>Merges a JSON payload into <paramref name="baseCatalog"/> by group id.</summary>
    public static ScanRootsCatalog Merge(ScanRootsCatalog baseCatalog, string json)
    {
        var overrides = JsonSerializer.Deserialize<ScanRootsCatalog>(json, JsonOptions);
        if (overrides?.Groups is not { Count: > 0 })
        {
            return baseCatalog;
        }

        var merged = new Dictionary<string, ScanRootGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in baseCatalog.Groups)
        {
            merged[group.Id] = group;
        }

        foreach (var group in overrides.Groups)
        {
            if (!string.IsNullOrWhiteSpace(group.Id))
            {
                merged[group.Id] = group;
            }
        }

        return new ScanRootsCatalog { Groups = [.. merged.Values] };
    }

    /// <summary>Resolves all active root directories for the current OS and tiers.</summary>
    public IReadOnlyList<string> Resolve(bool includeMedium, bool includeAdvanced)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var roots = new SortedSet<string>(comparer);

        foreach (var group in Groups)
        {
            if (group.Tier == RootTier.Medium && !includeMedium)
            {
                continue;
            }

            if (group.Tier == RootTier.Advanced && !includeAdvanced)
            {
                continue;
            }

            if (!MatchesCurrentOs(group.Os))
            {
                continue;
            }

            var basePath = ResolveBase(group.Base);
            if (string.IsNullOrWhiteSpace(basePath))
            {
                continue;
            }

            foreach (var sub in group.Subdirectories.Length == 0 ? [""] : group.Subdirectories)
            {
                var root = group.Join == SubPathJoin.Combine
                    ? Path.Combine(basePath, sub)
                    : basePath + sub;
                roots.Add(root);
            }
        }

        return [.. roots.Where(Directory.Exists)];
    }

    internal static bool MatchesCurrentOs(string[] os)
    {
        if (os is not { Length: > 0 })
        {
            return true;
        }

        foreach (var name in os)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "windows" when OperatingSystem.IsWindows():
                case "linux" when OperatingSystem.IsLinux():
                case "macos" or "osx" when OperatingSystem.IsMacOS():
                    return true;
            }
        }

        return false;
    }

    /// <summary>Resolves a base-directory token; returns null when unavailable on this machine.</summary>
    internal static string? ResolveBase(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var trimmed = token.Trim();
        if (trimmed.StartsWith('$'))
        {
            switch (trimmed.ToUpperInvariant())
            {
                case "$TEMP":
                    // Path.GetTempPath honors TMPDIR/TEMP/TMP per OS and always
                    // yields an existing directory (env vars are absent on CI/Linux)
                    return Path.GetTempPath();
                case "$TMPDIR":
                    return Environment.GetEnvironmentVariable("TMPDIR")
                        ?? Environment.GetEnvironmentVariable("TEMP")
                        ?? Environment.GetEnvironmentVariable("TMP")
                        ?? Path.GetTempPath();
                case "$LOCALAPPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local
                        ? local
                        : null;
                case "$APPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) is { Length: > 0 } roaming
                        ? roaming
                        : null;
                case "$PROFILE":
                    return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile
                        ? profile
                        : null;
                case "$WINDIR":
                    return GetWindowsDir();
                case "$SYSTEMDRIVE":
                {
                    var windowsDir = GetWindowsDir();
                    return windowsDir is null ? null : Path.GetPathRoot(windowsDir);
                }
                case "$CACHE":
                    return ResolveCacheDir();
                default:
                    return null;
            }
        }

        return trimmed;
    }

    private static string? GetWindowsDir()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return Environment.GetEnvironmentVariable("WINDIR")
            ?? Path.GetDirectoryName(Environment.SystemDirectory)
            ?? @"C:\Windows";
    }

    private static string? ResolveCacheDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cache))
        {
            cache = OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Caches")
                : Path.Combine(home, ".cache");
        }

        return cache;
    }

    private static ScanRootsCatalog LoadEmbedded()
    {
        var assembly = typeof(ScanRootsCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<ScanRootsCatalog>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidOperationException("Embedded scan-roots catalog failed to deserialize.");
    }
}
