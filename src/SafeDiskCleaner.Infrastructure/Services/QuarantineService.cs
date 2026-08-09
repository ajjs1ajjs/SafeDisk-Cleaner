using Microsoft.EntityFrameworkCore;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Infrastructure.Data;

namespace SafeDiskCleaner.Infrastructure.Services;

public sealed class QuarantineService : IQuarantineService
{
    private readonly IAppPaths _paths;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public QuarantineService(IAppPaths paths, IDbContextFactory<AppDbContext> dbFactory)
    {
        _paths = paths;
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entities = await db.Quarantines
            .AsNoTracking()
            .OrderByDescending(e => e.QuarantinedAt)
            .ToListAsync(ct);
        return entities
            .Select(e => new QuarantineEntry
            {
                Id = e.Id,
                OriginalPath = e.OriginalPath,
                QuarantinedPath = e.StoredName,
                Size = e.Size,
                QuarantinedAt = e.QuarantinedAt,
                ExpiresAt = e.ExpiresAt,
            })
            .ToList();
    }

    public async Task<string> QuarantineAsync(string sourcePath, uint retentionDays, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var targetDir = Path.Combine(_paths.QuarantineDir, id);
        Directory.CreateDirectory(targetDir);

        var name = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(name))
        {
            name = "file";
        }

        var dest = Path.Combine(targetDir, name);
        var size = new FileInfo(sourcePath).Length;

        MoveAcrossVolumes(sourcePath, dest);

        var now = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Quarantines.Add(new QuarantineEntity
        {
            Id = id,
            OriginalPath = sourcePath,
            StoredName = name,
            Size = size,
            QuarantinedAt = now,
            ExpiresAt = now.AddDays(retentionDays),
        });
        await db.SaveChangesAsync(ct);

        return id;
    }

    public async Task RestoreAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Quarantines.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            throw new InvalidOperationException("Quarantine entry not found");
        }

        var stored = Path.Combine(_paths.QuarantineDir, id, entity.StoredName);
        if (!File.Exists(stored))
        {
            throw new InvalidOperationException("Stored file not found");
        }

        var original = entity.OriginalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(original) ?? string.Empty);

        if (File.Exists(original))
        {
            throw new InvalidOperationException("Target path already exists; restore refused");
        }

        File.Move(stored, original);

        db.Quarantines.Remove(entity);
        await db.SaveChangesAsync(ct);

        Directory.Delete(Path.Combine(_paths.QuarantineDir, id), recursive: true);
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Quarantines.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is not null)
        {
            db.Quarantines.Remove(entity);
            await db.SaveChangesAsync(ct);
        }

        var dir = Path.Combine(_paths.QuarantineDir, id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public async Task<int> PurgeExpiredAsync(uint retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var expired = await db.Quarantines
            .Where(e => e.QuarantinedAt <= cutoff)
            .ToListAsync(ct);

        foreach (var entity in expired)
        {
            var dir = Path.Combine(_paths.QuarantineDir, entity.Id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            db.Quarantines.Remove(entity);
        }

        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task<int> EmptyAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var all = await db.Quarantines.ToListAsync(ct);

        foreach (var entity in all)
        {
            var dir = Path.Combine(_paths.QuarantineDir, entity.Id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        db.Quarantines.RemoveRange(all);
        await db.SaveChangesAsync(ct);
        return all.Count;
    }

    /// <summary>
    /// Moves a file. <see cref="File.Move"/> cannot cross volume boundaries
    /// (error 17 / ERROR_NOT_SAME_DEVICE), so falls back to copy + delete.
    /// </summary>
    private static void MoveAcrossVolumes(string source, string dest)
    {
        try
        {
            File.Move(source, dest);
        }
        catch (IOException e) when (e.HResult is 17 or unchecked((int)0x80070011))
        {
            File.Copy(source, dest, overwrite: false);
            File.Delete(source);
        }
    }
}
