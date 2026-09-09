using FluentAssertions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Update;

namespace SafeDiskCleaner.Tests;

public sealed class UpdateAssetsTests
{
    private static ReleaseAsset Asset(string name) => new()
    {
        Name = name,
        DownloadUrl = $"https://example.com/{name}",
        Size = 1024,
    };

    private static UpdateInfo Info(params string[] names) => new()
    {
        Available = true,
        LatestVersion = "v9.9.9",
        CurrentVersion = "0.0.0",
        DownloadUrl = "https://example.com/release",
        Assets = names.Select(Asset).ToList(),
    };

    [Fact]
    public void SelectInstallAsset_SkipsChecksumFiles_EvenWhenListedFirst()
    {
        // Regression: "<asset>.sha256" contains the platform hint too and must
        // never be picked as the install asset.
        var install = PlatformAssetName();
        var assets = Info(
            install + ".sha256",
            "SafeDiskCleaner-1.7.3-setup-win64.exe",
            install).Assets;

        var selected = UpdateAssets.SelectInstallAsset(assets);

        selected.Should().NotBeNull();
        selected!.Name.Should().Be(install);
    }

    [Fact]
    public void SelectInstallAsset_ReturnsNull_WhenNoPlatformAsset()
    {
        // Old releases (<=1.7.2) ship no portable asset: the UI must fall back
        // to the browser instead of downloading a wrong file. The setup exe
        // matches no platform hint on any OS.
        var assets = Info("SafeDiskCleaner-1.7.2-setup-win64.exe").Assets;

        UpdateAssets.SelectInstallAsset(assets).Should().BeNull();
    }

    private static string PlatformAssetName() =>
        OperatingSystem.IsWindows() ? "SafeDiskCleaner-1.7.3-portable-win64.exe"
        : OperatingSystem.IsMacOS() ? "SafeDiskCleaner-1.7.3-macos-x64.tar.gz"
        : "SafeDiskCleaner-1.7.3-linux-x64.tar.gz";

    [Fact]
    public void SelectChecksumAsset_FindsCompanion_ForSelectedAsset()
    {
        var install = PlatformAssetName();
        var assets = Info(install, install + ".sha256").Assets;

        var checksum = UpdateAssets.SelectChecksumAsset(assets);

        checksum.Should().NotBeNull();
        checksum!.Name.Should().Be(install + ".sha256");
    }

    [Fact]
    public void SelectChecksumAsset_ReturnsNull_WhenReleaseShipsNone()
    {
        var assets = Info("SafeDiskCleaner-1.7.3-portable-win64.exe").Assets;

        UpdateAssets.SelectChecksumAsset(assets).Should().BeNull();
    }
}
