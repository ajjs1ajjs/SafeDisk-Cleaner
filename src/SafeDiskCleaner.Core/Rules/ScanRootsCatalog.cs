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
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // invalid/unreadable override — fall back to defaults
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
            if (string.IsNullOrWhiteSpace(group.Id))
            {
                continue;
            }

            // An override file is user-writable: reject groups whose resolved
            // roots escape into protected locations. The scanner validates
            // roots again at scan time (defense in depth).
            if (GroupTouchesProtected(group))
            {
                continue;
            }

            merged[group.Id] = group;
        }

        return new ScanRootsCatalog { Groups = [.. merged.Values] };
    }

    private static bool GroupTouchesProtected(ScanRootGroup group)
    {
        var basePath = ResolveBase(group.Base);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return true; // unresolvable base — drop the group
        }

        foreach (var sub in group.Subdirectories.Length == 0 ? [""] : group.Subdirectories)
        {
            string root;
            try
            {
                root = group.Join == SubPathJoin.Combine && !Path.IsPathFullyQualified(sub)
                    ? Path.GetFullPath(Path.Combine(basePath, sub))
                    : Path.GetFullPath(basePath + sub);
            }
            catch
            {
                return true;
            }

            if (Models.PathProtection.IsProtectedPath(root))
            {
                return true;
            }
        }

        return false;
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
                string root;
                if (group.Join == SubPathJoin.Combine)
                {
                    // Combine mode: never let an absolute sub discard the base.
                    if (Path.IsPathFullyQualified(sub))
                        continue;
                    root = Path.Combine(basePath, sub);
                }
                else
                {
                    root = basePath + sub;
                }
                // Canonicalize and confine: the resolved root must stay
                // inside its base (`..\..` escapes are rejected).
                string fullBase, fullRoot;
                try
                {
                    fullBase = Path.GetFullPath(basePath);
                    fullRoot = Path.GetFullPath(root);
                }
                catch
                {
                    continue;
                }
                var cmp = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!fullRoot.Equals(fullBase, cmp)
                    && !fullRoot.StartsWith(fullBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, cmp))
                {
                    continue;
                }
                roots.Add(fullRoot);
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
                    // Env-overridable by design, so verify the result: it must
                    // exist and must not itself be a protected location.
                    var tmpdir = Environment.GetEnvironmentVariable("TMPDIR")
                        ?? Environment.GetEnvironmentVariable("TEMP")
                        ?? Environment.GetEnvironmentVariable("TMP")
                        ?? Path.GetTempPath();
                    try
                    {
                        if (Directory.Exists(tmpdir)
                            && !Models.PathProtection.IsProtectedPath(tmpdir))
                            return tmpdir;
                    }
                    catch
                    {
                        // fall through to the safe default below
                    }
                    return Path.GetTempPath();
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
