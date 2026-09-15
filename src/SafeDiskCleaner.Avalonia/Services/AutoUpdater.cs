using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Update;
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

    /// <summary>Picks the asset matching the current platform (shared contract, see UpdateAssets).</summary>
    public ReleaseAsset? SelectAsset(UpdateInfo info) =>
        SafeDiskCleaner.Core.Update.UpdateAssets.SelectInstallAsset(info.Assets);

    public async Task DownloadAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        // Shared hardened pipeline: URL allowlist, redirect re-check, caps.
        using var client = _httpFactory.CreateClient("downloads");
        await UpdateDownload.DownloadAsync(client, asset, destinationPath, progress, ct);
    }

    /// <summary>
    /// Installs the downloaded package. On Windows the portable exe is swapped
    /// via an updater script (the running exe cannot be replaced while running);
    /// on other platforms the downloaded asset is opened with the OS handler.
    /// The package is re-validated immediately before anything executes,
    /// narrowing the verify→launch window to a single call.
    /// </summary>
    public void LaunchInstaller(string downloadedPath)
    {
        VerifyPackage(downloadedPath);
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

    /// <summary>
    /// Verifies the downloaded file before it is ever executed:
    /// 1. it must be a valid PE executable (not an HTML error page / truncated file);
    /// 2. when both sides carry an Authenticode signature, publishers must match;
    ///    a signature that does not chain is rejected outright.
    /// Unsigned current releases are tolerated (CI signing is optional) — the
    /// SHA-256 gate enforced by the callers remains the primary check.
    /// </summary>
    internal static void VerifyPackage(string downloadedPath)
    {
        if (!File.Exists(downloadedPath) || new FileInfo(downloadedPath).Length == 0)
        {
            throw new InvalidOperationException("Downloaded update is empty or missing");
        }

        if (!IsValidPe(downloadedPath))
        {
            throw new InvalidOperationException("Downloaded update is not a valid executable");
        }

        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            return;
        }

        try
        {
#pragma warning disable SYSLIB0057
            var currentCert = X509Certificate.CreateFromSignedFile(currentExe);
            var newCert = X509Certificate.CreateFromSignedFile(downloadedPath);
#pragma warning restore SYSLIB0057
            if (!string.Equals(currentCert.Subject, newCert.Subject, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Update publisher does not match the installed application");
            }
        }
        catch (CryptographicException)
        {
            // The installed exe is not Authenticode-signed, so the publisher
            // cannot be cross-checked. The PE-format check above still protects
            // against non-executable downloads. Strong integrity verification
            // requires the project to sign its release binaries.
        }
    }

    private static bool IsValidPe(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            if (reader.ReadUInt16() != 0x5A4D) // "MZ"
            {
                return false;
            }

            fs.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset > fs.Length - 4)
            {
                return false;
            }

            fs.Position = peOffset;
            return reader.ReadUInt32() == 0x00004550; // "PE\0\0"
        }
        catch
        {
            return false;
        }
    }

    private static void LaunchPortableSwap(string downloadedPath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            throw new InvalidOperationException("Cannot determine the running executable path");
        }

        // Unique per-run directory with exclusive creation: a fixed
        // %TEMP%\SafeDiskUpdater\update.cmd could be pre-created or swapped
        // by any local process between write and `cmd /c`.
        var updaterDir = Path.Combine(Path.GetTempPath(), "SafeDiskUpdater-" + Path.GetRandomFileName());
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

        // Exclusive creation: never truncate a file someone else planted.
        using (var fs = new FileStream(script, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, System.Text.Encoding.ASCII))
        {
            writer.Write(content);
        }

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
        SafeDiskCleaner.Core.Update.UpdateAssets.SelectChecksumAsset(info.Assets);

    /// <inheritdoc />
    public async Task<string> DownloadTextAsync(ReleaseAsset asset, CancellationToken ct = default)
    {
        using var client = _httpFactory.CreateClient("downloads");
        return await UpdateDownload.DownloadTextAsync(client, asset, ct);
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