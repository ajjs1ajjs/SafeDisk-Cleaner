using Refit;

namespace SafeDiskCleaner.Infrastructure.Services;

public sealed record GitHubAsset(
    [property: AliasAs("name")] string? Name,
    [property: AliasAs("browser_download_url")] string? BrowserDownloadUrl,
    [property: AliasAs("size")] long Size);

public sealed record GitHubRelease(
    [property: AliasAs("tag_name")] string? TagName,
    [property: AliasAs("html_url")] string? HtmlUrl,
    [property: AliasAs("assets")] IReadOnlyList<GitHubAsset>? Assets);

public interface IGitHubApi
{
    [Get("/repos/{ownerRepo}/releases/latest")]
    [Headers("Accept: application/vnd.github+json", "User-Agent: SafeDisk-Cleaner")]
    Task<GitHubRelease> GetLatestRelease(string ownerRepo, CancellationToken cancellationToken);
}

public static class SemanticVersion
{
    /// <summary>
    /// Parses "major.minor.patch" allowing pre-release suffixes such as
    /// "2.0.0-beta.1" (the numeric prefix of each segment is used, so the
    /// pre-release is ignored for comparison). Returns null for non-versions.
    /// </summary>
    public static (int Major, int Minor, int Patch)? TryParse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var value = version.TrimStart('v', 'V');
        var parts = value.Split('.');
        if (parts.Length < 3)
        {
            return null;
        }

        if (!TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor)
            || !TryParsePart(parts[2], out var patch))
        {
            return null;
        }

        return (major, minor, patch);
    }

    private static bool TryParsePart(string part, out int value)
    {
        value = 0;
        var i = 0;
        while (i < part.Length && char.IsAsciiDigit(part[i]))
        {
            i++;
        }

        return i > 0 && int.TryParse(part.AsSpan(0, i), out value);
    }

    public static bool IsNewerThan(string? candidate, string current)
    {
        var a = TryParse(candidate);
        var b = TryParse(current);
        if (a is null || b is null)
        {
            return false;
        }

        return a.Value.CompareTo(b.Value) > 0;
    }
}
