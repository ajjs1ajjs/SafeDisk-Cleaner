using System.Diagnostics;
using System.IO;
using System.Net.Http;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Utils;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Downloads and installs a newer release from GitHub.
/// Windows: downloads the portable exe and swaps it via an updater script.
/// Linux/macOS: downloads the matching asset and hands it to the OS; the
/// current process is not self-replacing there.
/// </summary>
public sealed class AutoUpdater : SafeDiskCleaner.ViewModels.Abstractions.IUpdateInstaller
{
    private readonly IUpdateService _update;
    private readonly IHttpClientFactory _httpFactory;

    public AutoUpdater(IUpdateService update, IHttpClientFactory httpFactory)
    {
        _update = update;
        _httpFactory = httpFactory;
    }

    public Task<UpdateInfo> CheckAsync(CancellationToken ct = default) =>
        _update.CheckAsync(ct);

    /// <summary>Picks the asset matching the current platform.</summary>
    public ReleaseAsset? SelectAsset(UpdateInfo info)
    {
        var hints = OperatingSystem.IsWindows()
            ? new[] { "portable" }
            : OperatingSystem.IsMacOS()
                ? new[] { "dmg", "macos", "osx" }
                : new[] { "AppImage", "linux", "tar.gz" };

        return info.Assets.FirstOrDefault(a =>
            hints.Any(h => a.Name.Contains(h, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task DownloadAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        // Large payload client (no 8s total timeout) configured in DI.
        using var client = _httpFactory.CreateClient("downloads");
        using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : 0);
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                progress?.Report(Math.Min(100.0, read * 100.0 / total));
            }
        }
    }

    /// <summary>
    /// Installs the downloaded package. On Windows the portable exe is swapped
    /// via an updater script (the running exe cannot be replaced while running);
    /// on other platforms the downloaded asset is opened with the OS handler.
    /// </summary>
    public void LaunchInstaller(string downloadedPath)
    {
        if (OperatingSystem.IsWindows())
        {
            LaunchPortableSwap(downloadedPath);
            return;
        }

        if (!File.Exists(downloadedPath))
        {
            throw new InvalidOperationException("Downloaded update is missing");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadedPath,
                UseShellExecute = true,
            });
        }
        catch
        {
            // No default handler for the asset — the file remains in temp for
            // the user to open manually.
        }
    }

    private static void LaunchPortableSwap(string downloadedPath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            throw new InvalidOperationException("Cannot determine the running executable path");
        }

        var updaterDir = Path.Combine(Path.GetTempPath(), "SafeDiskUpdater");
        Directory.CreateDirectory(updaterDir);
        var script = Path.Combine(updaterDir, "update.cmd");

        // Paths are quoted and %% is doubled for batch-safe interpolation.
        var content =
            "@echo off\r\n" +
            "setlocal DisableDelayedExpansion\r\n" +
            ":wait\r\n" +
            "tasklist /fi \"IMAGENAME eq SafeDiskCleaner.exe\" | find /i \"SafeDiskCleaner.exe\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            $"copy /y \"{EscapeForBatch(downloadedPath)}\" \"{EscapeForBatch(currentExe)}\" >nul\r\n" +
            $"del /q \"{EscapeForBatch(downloadedPath)}\"\r\n" +
            $"start \"\" \"{EscapeForBatch(currentExe)}\"\r\n" +
            "del \"%~f0\"\r\n";

        File.WriteAllText(script, content);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static string EscapeForBatch(string path) =>
        path.Replace("%", "%%").Replace("^", "^^");

    /// <summary>Finds the "<asset>.sha256" companion asset, or null when the release ships none.</summary>
    public ReleaseAsset? SelectChecksumAsset(UpdateInfo info) =>
        SelectAsset(info) is { } asset
            ? info.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase))
            : null;

    /// <inheritdoc />
    public async Task<string> DownloadTextAsync(ReleaseAsset asset, CancellationToken ct = default)
    {
        using var client = _httpFactory.CreateClient("downloads");
        return await client.GetStringAsync(asset.DownloadUrl, ct);
    }

    /// <summary>
    /// Verifies the SHA-256 of the downloaded file against a checksum-file
    /// payload. Throws and deletes the file when verification fails — a
    /// tampered or truncated download must never be executed.
    /// </summary>
    public void VerifySha256(string downloadedPath, string checksumPayload)
    {
        var expected = Sha256Checksum.Parse(checksumPayload);
        if (expected is null)
        {
            throw new InvalidOperationException("Checksum payload did not contain a valid SHA-256 digest");
        }

        var actual = Sha256Checksum.ComputeFile(downloadedPath);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteQuietly(downloadedPath);
            throw new InvalidOperationException(
                $"SHA-256 mismatch for the downloaded update (expected {expected}, got {actual}). The file was deleted.");
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort — the temp file will be cleaned up by the OS
        }
    }
}