using System.Text.RegularExpressions;

namespace SafeDiskCleaner.Core.Update;

/// <summary>
/// Allowlist validation for updater URLs. Asset URLs arrive inside the
/// (attacker-influenceable) GitHub API payload, and checksum URLs offer no
/// origin authentication on their own — so every fetch target is validated
/// before any request, and redirects are re-checked by the callers.
/// </summary>
public static partial class UpdateUrlValidator
{
    private static readonly string[] AllowedHosts =
    [
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    private const string Owner = "ajjs1ajjs";
    private const string Repo = "SafeDisk-Cleaner";

    /// <summary>True for an https GitHub asset/release URL with no userinfo.</summary>
    public static bool IsAllowedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return false;
        if (uri.UserInfo.Length > 0 || !uri.IsDefaultPort)
            return false;
        if (uri.HostNameType != UriHostNameType.Dns)
            return false;
        return AllowedHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True for the project's own releases page (used for "open in browser" fallback).</summary>
    public static bool IsAllowedReleasePageUrl(string? url)
    {
        if (!IsAllowedDownloadUrl(url))
            return false;
        var uri = new Uri(url!);
        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Asset filenames must be plain names (no traversal, ADS, separators).</summary>
    public static bool IsSafeAssetName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 200
        && AssetNameRegex().IsMatch(name);

    /// <summary>Release tags embedded in paths/UI must look like versions.</summary>
    public static bool IsSafeVersionTag(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && VersionTagRegex().IsMatch(tag);

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex AssetNameRegex();

    [GeneratedRegex(@"^[vV]?\d+\.\d+\.\d+([-+.0-9A-Za-z]+)?$")]
    private static partial Regex VersionTagRegex();
}
