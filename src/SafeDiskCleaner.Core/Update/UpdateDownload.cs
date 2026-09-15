using System.Net.Http;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Core.Update;

/// <summary>
/// Shared, hardened download primitives for both UI flavors (WPF + Avalonia).
/// Centralizing here keeps the verify→launch chain identical everywhere so a
/// ViewModel cannot accidentally skip a step.
/// </summary>
public static class UpdateDownload
{
    /// <summary>Upper bound for checksum sidecar payloads (they are tiny).</summary>
    public const int MaxChecksumBytes = 8 * 1024;

    /// <summary>Hard ceiling for release binaries (disk-exhaustion guard).</summary>
    public const long MaxBinaryBytes = 500L * 1024 * 1024;

    /// <summary>Headroom above the manifest size for compression variance.</summary>
    private const long SizeSlackBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Creates a non-guessable temp destination for an asset: random subdir,
    /// validated filename, exclusive creation. Predictable `%TEMP%` paths
    /// allowed pre-creation/symlink squats and verify→launch swaps.
    /// </summary>
    public static string CreateSecureTempPath(string assetName, string version)
    {
        if (!UpdateUrlValidator.IsSafeAssetName(assetName))
        {
            throw new InvalidOperationException("Unsafe asset filename from release metadata");
        }

        if (!UpdateUrlValidator.IsSafeVersionTag(version))
        {
            throw new InvalidOperationException("Unsafe release version from metadata");
        }

        var dir = Path.Combine(Path.GetTempPath(), "SafeDisk-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, assetName);
    }

    /// <summary>
    /// Downloads an asset with URL allowlisting, redirect re-validation and
    /// size caps. Throws on any violation; partial files are deleted.
    /// </summary>
    public static async Task DownloadAsync(
        HttpClient client,
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        if (!UpdateUrlValidator.IsAllowedDownloadUrl(asset.DownloadUrl))
        {
            throw new InvalidOperationException("Release asset URL is not allowlisted");
        }

        long cap = asset.Size > 0
            ? Math.Min(asset.Size + SizeSlackBytes, MaxBinaryBytes)
            : MaxBinaryBytes;

        using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Re-validate after redirects: HttpClient follows cross-host ones.
        var finalUri = response.RequestMessage?.RequestUri?.ToString();
        if (!UpdateUrlValidator.IsAllowedDownloadUrl(finalUri))
        {
            throw new InvalidOperationException("Update download redirected off the allowlist");
        }

        if (response.Content.Headers.ContentLength is { } declared && declared > cap)
        {
            throw new InvalidOperationException("Release asset exceeds the size limit");
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        long read = 0;
        var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : 0);
        int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            read += n;
            if (read > cap)
            {
                throw new InvalidOperationException("Release asset exceeds the size limit");
            }

            await dest.WriteAsync(buffer.AsMemory(0, n), ct);
            if (total > 0)
            {
                progress?.Report(Math.Min(100.0, read * 100.0 / total));
            }
        }
    }

    /// <summary>Downloads a small text payload (checksum sidecar), capped.</summary>
    public static async Task<string> DownloadTextAsync(HttpClient client, ReleaseAsset asset, CancellationToken ct = default)
    {
        if (!UpdateUrlValidator.IsAllowedDownloadUrl(asset.DownloadUrl))
        {
            throw new InvalidOperationException("Checksum URL is not allowlisted");
        }

        using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } declared && declared > MaxChecksumBytes)
        {
            throw new InvalidOperationException("Checksum payload exceeds the size limit");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > MaxChecksumBytes)
        {
            throw new InvalidOperationException("Checksum payload exceeds the size limit");
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
