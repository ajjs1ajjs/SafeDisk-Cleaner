using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Core.Abstractions;

/// <summary>Resolves the application's data directories (audit, quarantine, reports).</summary>
public interface IAppPaths
{
    string DataRoot { get; }
    string AuditDir { get; }
    string QuarantineDir { get; }
    string ReportsDir { get; }

    void EnsureCreated();
}

/// <summary>Persists the audit log (JSONL in the original; EF Core + SQLite in the rewrite).</summary>
public interface IAuditService
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> GetAllAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>Manages the quarantine: files moved aside before permanent deletion.</summary>
public interface IQuarantineService
{
    Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken ct = default);
    Task<string> QuarantineAsync(string sourcePath, uint retentionDays, CancellationToken ct = default);
    Task RestoreAsync(string id, CancellationToken ct = default);
    Task RemoveAsync(string id, CancellationToken ct = default);
    Task<int> PurgeExpiredAsync(uint retentionDays, CancellationToken ct = default);
    Task<int> EmptyAsync(CancellationToken ct = default);
}

/// <summary>Checks the GitHub releases API for a newer version.</summary>
public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken ct = default);
}

/// <summary>Writes JSON reports to the reports directory.</summary>
public interface IReportWriter
{
    Task<string> WriteCleanupReportAsync(CleanupResult result, CancellationToken ct = default);
    Task<string> WriteScanReportAsync(ScanResult result, CancellationToken ct = default);
}
