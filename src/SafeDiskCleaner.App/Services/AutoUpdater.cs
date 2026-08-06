using System.Diagnostics;
using System.IO;
using System.Net.Http;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.App.Services;

/// <summary>
/// Downloads and installs a newer release from GitHub.
/// - Installed (Program Files): downloads the Setup bootstrapper and launches it.
/// - Portable: downloads the portable exe and swaps it via an updater script
///   (the running exe cannot be replaced while running).
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

    public bool IsRunningFromInstalledLocation
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return baseDir.StartsWith(pf, StringComparison.OrdinalIgnoreCase)
                || baseDir.StartsWith(pfX86, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default) =>
        await _update.CheckAsync(ct);

    /// <summary>Picks the release asset that matches the current deployment type.</summary>
    public ReleaseAsset? SelectAsset(UpdateInfo info)
    {
        if (IsRunningFromInstalledLocation)
        {
            return info.Assets.FirstOrDefault(a => a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));
        }

        return info.Assets.FirstOrDefault(a => a.Name.Contains("portable", StringComparison.OrdinalIgnoreCase));
    }

    public async Task DownloadAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        using var client = _httpFactory.CreateClient("github");
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
    /// Installs the downloaded package. Returns after launching the update flow;
    /// the caller should then shut the application down.
    /// </summary>
    public void LaunchInstaller(string downloadedPath)
    {
        if (IsRunningFromInstalledLocation)
        {
            Process.Start(new ProcessStartInfo(downloadedPath) { UseShellExecute = true });
            return;
        }

        LaunchPortableSwap(downloadedPath);
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

        var content =
            "@echo off\r\n" +
            ":wait\r\n" +
            "tasklist /fi \"IMAGENAME eq SafeDiskCleaner.exe\" | find /i \"SafeDiskCleaner.exe\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            $"copy /y \"{downloadedPath}\" \"{currentExe}\" >nul\r\n" +
            $"del /q \"{downloadedPath}\"\r\n" +
            $"start \"\" \"{currentExe}\"\r\n" +
            "del \"%~f0\"\r\n";

        File.WriteAllText(script, content);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
