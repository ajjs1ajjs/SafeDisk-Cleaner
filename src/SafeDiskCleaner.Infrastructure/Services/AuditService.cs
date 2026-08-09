using Microsoft.EntityFrameworkCore;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Infrastructure.Data;

namespace SafeDiskCleaner.Infrastructure.Services;

public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public AuditService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.AuditLogs.Add(new AuditLogEntry
        {
            Timestamp = entry.Timestamp,
            Action = entry.Action,
            Path = entry.Path,
            Size = entry.Size,
            Success = entry.Success,
            Detail = entry.Detail,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entities = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(ct);
        return entities
            .Select(e => new AuditEntry
            {
                Timestamp = e.Timestamp,
                Action = e.Action,
                Path = e.Path,
                Size = e.Size,
                Success = e.Success,
                Detail = e.Detail,
            })
            .ToList();
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.AuditLogs.ExecuteDeleteAsync(ct);
    }
}
