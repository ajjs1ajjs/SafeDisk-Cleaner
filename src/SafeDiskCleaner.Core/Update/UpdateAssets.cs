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
        var hints = OperatingSystem.IsWindows()
            ? ["portable"]
            : OperatingSystem.IsMacOS()
                ? (string[])["dmg", "macos", "osx"]
                : ["AppImage", "linux", "tar.gz"];

        return assets.FirstOrDefault(a =>
            !IsChecksumFile(a.Name)
            && hints.Any(h => a.Name.Contains(h, StringComparison.OrdinalIgnoreCase)));
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
