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
        // Read the size before the move. Guard against the TOCTOU race where the
        // file is removed between measuring and moving, which used to surface as
        // an uncaught FileNotFoundException and abort the whole cleanup run.
        long size;
        try
        {
            size = new FileInfo(sourcePath).Length;
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException($"Source file no longer exists: {sourcePath}");
        }

        MoveAcrossVolumes(sourcePath, dest);

        var now = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        try
        {
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
        }
        catch
        {
            // The file has already been moved. If we cannot record the DB row,
            // move it back so it is not orphaned in the quarantine directory.
            try
            {
                if (File.Exists(dest) && !File.Exists(sourcePath))
                {
                    File.Move(dest, sourcePath);
                }

                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, recursive: true);
                }
            }
            catch
            {
                // Rollback is best-effort; the original exception is more useful.
            }

            throw;
        }

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
        var originalDir = Path.GetDirectoryName(original);
        if (!string.IsNullOrEmpty(originalDir))
        {
            Directory.CreateDirectory(originalDir);
        }

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

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        // Purge by the entry's own ExpiresAt (set when it was quarantined),
        // not by a re-derived cutoff from a caller-supplied retention value.
        // A caller passing a different retention would otherwise purge entries
        // too early or keep them too long.
        var now = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var expired = await db.Quarantines
            .Where(e => e.ExpiresAt <= now)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var entity in expired)
        {
            var dir = Path.Combine(_paths.QuarantineDir, entity.Id);
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                    // A locked/blocked file — skip the DB row too so the entry
                    // is not reported as purged while its data is still on disk.
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            db.Quarantines.Remove(entity);
            purged++;
        }

        await db.SaveChangesAsync(ct);
        return purged;
    }

    public async Task<int> EmptyAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var all = await db.Quarantines.ToListAsync(ct);

        var emptied = 0;
        foreach (var entity in all)
        {
            var dir = Path.Combine(_paths.QuarantineDir, entity.Id);
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            db.Quarantines.Remove(entity);
            emptied++;
        }

        await db.SaveChangesAsync(ct);
        return emptied;
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
