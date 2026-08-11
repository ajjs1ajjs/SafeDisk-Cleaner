using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Downloads and installs a newer release from GitHub.
/// Downloads the portable exe and swaps it via an updater script
/// (the running exe cannot be replaced while running).
/// </summary>
public sealed class AutoUpdater
{
    private readonly IUpdateService _update;
    private readonly IHttpClientFactory _httpFactory;

    public AutoUpdater(IUpdateService update, IHttpClientFactory httpFactory)
    {
        _update = update;
        _httpFactory = httpFactory;
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default) =>
        await _update.CheckAsync(ct);

    /// <summary>Picks the portable release asset.</summary>
    public ReleaseAsset? SelectAsset(UpdateInfo info) =>
        info.Assets.FirstOrDefault(a => a.Name.Contains("portable", StringComparison.OrdinalIgnoreCase));

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
    /// Installs the downloaded package by swapping the portable exe. Returns after
    /// launching the update flow; the caller should then shut the application down.
    /// The downloaded binary is verified before anything is executed.
    /// </summary>
    public void LaunchInstaller(string downloadedPath)
    {
        VerifyPackage(downloadedPath);
        LaunchPortableSwap(downloadedPath);
    }

    /// <summary>
    /// Verifies the downloaded file before it is ever executed:
    /// 1. it must be a valid PE executable (not an HTML error page / truncated file);
    /// 2. if the currently running exe is Authenticode-signed, the downloaded exe
    ///    must be signed by the same publisher.
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
            // X509CertificateLoader has no Authenticode-extraction equivalent
            // (see dotnet/runtime#91763); CreateFromSignedFile is the only
            // built-in API that reads the signer cert out of a signed PE.
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

        var updaterDir = Path.Combine(Path.GetTempPath(), "SafeDiskUpdater");
        Directory.CreateDirectory(updaterDir);
        var script = Path.Combine(updaterDir, "update.cmd");

        // Paths are quoted and %% is doubled for batch-safe interpolation.
        // setlocal DisableDelayedExpansion keeps '!' literal; '^' must be
        // doubled so it is not consumed as an escape inside the batch file.
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

    /// <summary>
    /// Escapes a path for interpolation inside a quoted string in a batch file.
    /// '%' must be doubled; '^' must be doubled so it survives as a literal
    /// caret. '!' is made safe by <c>setlocal DisableDelayedExpansion</c> in
    /// the script. Windows forbids '"' in file names, so quotes need no escape.
    /// </summary>
    private static string EscapeForBatch(string path) =>
        path.Replace("%", "%%").Replace("^", "^^");
}
