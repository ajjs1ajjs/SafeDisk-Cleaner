using System.Runtime.InteropServices;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Core.Update;

/// <summary>
/// Shared release-asset selection for the in-app auto-update flow.
///
/// Contract with CI (.github/workflows/ci.yml): the release must contain a
/// per-OS install asset plus a "<asset>.sha256" checksum companion. Checksum
/// files are never install candidates — they only feed <see cref="SelectChecksumAsset"/>.
/// </summary>
public static class UpdateAssets
{
    /// <summary>Picks the install asset for the current OS, or null when the release has none.</summary>
    public static ReleaseAsset? SelectInstallAsset(IReadOnlyList<ReleaseAsset> assets)
    {
        var candidates = assets.Where(a => !IsChecksumFile(a.Name)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return candidates.FirstOrDefault(a =>
                a.Name.Contains("portable", StringComparison.OrdinalIgnoreCase));
        }

        if (OperatingSystem.IsMacOS())
        {
            // "tar.gz" alone must never match: otherwise a linux asset listed first
            // would be picked on macOS (and vice versa). Require an OS marker.
            var mac = candidates.Where(a =>
                a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("osx", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)).ToList();
            if (mac.Count == 0)
            {
                return null;
            }

            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return mac.FirstOrDefault(a => a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
                    ?? mac.FirstOrDefault();
            }

            // Intel Mac: prefer an x64 asset that is not arm64, then any non-arm64, then whatever is left.
            return mac.FirstOrDefault(a =>
                    a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)
                    && !a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
                ?? mac.FirstOrDefault(a => !a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
                ?? mac.FirstOrDefault();
        }

        // Linux: same isolation rule — no bare "tar.gz" match.
        return candidates.FirstOrDefault(a =>
            a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase)
            || a.Name.Contains("AppImage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds the "<install-asset>.sha256" companion, or null when the release ships none.</summary>
    public static ReleaseAsset? SelectChecksumAsset(IReadOnlyList<ReleaseAsset> assets)
    {
        var install = SelectInstallAsset(assets);
        return install is null
            ? null
            : assets.FirstOrDefault(a =>
                string.Equals(a.Name, install.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsChecksumFile(string name) =>
        name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase);
}
