using System.Text.Json;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Infrastructure.Services;

public sealed class ReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAppPaths _paths;

    public ReportWriter(IAppPaths paths)
    {
        _paths = paths;
    }

    public async Task<string> WriteCleanupReportAsync(CleanupResult result, CancellationToken ct = default)
    {
        var file = Path.Combine(_paths.ReportsDir, $"cleanup-{DateTimeOffset.Now:yyyy-MM-dd}.json");
        await WriteAsync(file, result, ct);
        return file;
    }

    public async Task<string> WriteScanReportAsync(ScanResult result, CancellationToken ct = default)
    {
        var file = Path.Combine(_paths.ReportsDir, $"scan-{DateTimeOffset.Now:yyyy-MM-dd}.json");
        await WriteAsync(file, result, ct);
        return file;
    }

    private static async Task WriteAsync(string file, object value, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
    }
}
