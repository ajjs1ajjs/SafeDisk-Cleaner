using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Cleanup;

public sealed class CleanupEngine
{
    public const long LargeFileThreshold = 64L * 1024 * 1024;
    public const string RecycleBinSentinel = "__recycle_bin__";

    private readonly SafetyValidator _safety;
    private readonly IQuarantineService _quarantine;
    private readonly IAuditService _audit;

    public CleanupEngine(SafetyValidator safety, IQuarantineService quarantine, IAuditService audit)
    {
        _safety = safety;
        _quarantine = quarantine;
        _audit = audit;
    }

    public async Task<CleanupResult> RunAsync(
        IReadOnlyList<Candidate> candidates,
        CleanupOptions options,
        Action<CleanupProgress>? onProgress,
        CancellationToken ct = default)
    {
        await _quarantine.PurgeExpiredAsync(options.QuarantineRetentionDays, ct);

        var ordered = candidates
            .Where(c => c.Action != CandidateAction.Keep)
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => c.Size)
            .ToList();

        var total = (ulong)ordered.Count;
        var entries = new List<CleanupEntry>();
        long freed = 0;
        ulong processed = 0;

        foreach (var candidate in ordered)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            onProgress?.Invoke(new CleanupProgress
            {
                Processed = processed,
                Total = total,
                CurrentPath = candidate.Path,
                Status = options.Mode == CleanMode.DryRun ? "dry-run" : "cleaning",
                Percent = total == 0 ? 100.0 : processed * 100.0 / total,
                Finished = false,
            });

            if (options.Mode == CleanMode.DryRun)
            {
                freed += candidate.Size;
                entries.Add(new CleanupEntry
                {
                    Path = candidate.Path,
                    Size = candidate.Size,
                    Category = candidate.Category,
                    Confidence = candidate.Confidence,
                    Status = CleanupStatus.WouldDelete,
                    Detail = "Dry run — nothing was deleted",
                });
                continue;
            }

            var result = await ExecuteAsync(candidate, options, ct);
            var entry = new CleanupEntry
            {
                Path = candidate.Path,
                Size = candidate.Size,
                Category = candidate.Category,
                Confidence = candidate.Confidence,
                Status = result.Status,
                Detail = result.Status == CleanupStatus.Failed ? result.Error : result.Status.Description(),
            };

            entries.Add(entry);

            if (result.Status.IsSuccess())
            {
                freed += candidate.Size;
            }

            await _audit.AppendAsync(new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                Action = result.Status.AsString(),
                Path = candidate.Path,
                Size = candidate.Size,
                Success = result.Status.IsSuccess(),
                Detail = entry.Detail,
            }, ct);
        }

        onProgress?.Invoke(new CleanupProgress
        {
            Processed = processed,
            Total = total,
            CurrentPath = string.Empty,
            Status = "done",
            Percent = 100.0,
            Finished = true,
        });

        return new CleanupResult
        {
            Mode = options.Mode,
            Processed = ordered.Count,
            Deleted = entries.Count(e => e.Status.IsSuccess()),
            FreedBytes = freed,
            Entries = entries,
        };
    }

    private async Task<CleanupOutcome> ExecuteAsync(Candidate candidate, CleanupOptions options, CancellationToken ct)
    {
        if (options.Mode == CleanMode.Auto)
        {
            if (candidate.RiskLevel != RiskLevel.Safe)
            {
                return CleanupOutcome.Failed("Auto mode skips non-Safe risk level (needs review)");
            }

            if (candidate.Confidence < options.AutoThreshold)
            {
                return CleanupOutcome.Failed($"Confidence {candidate.Confidence} below auto threshold {options.AutoThreshold}");
            }
        }

        if (candidate.Category == Category.RecycleBin && candidate.Path == RecycleBinSentinel)
        {
            return WindowsApi.EmptyRecycleBin()
                ? CleanupOutcome.Success(CleanupStatus.Deleted)
                : CleanupOutcome.Failed("Failed to empty Recycle Bin");
        }

        var verdict = _safety.Validate(candidate.Path, candidate.Category, recencyDays: 3);
        if (!verdict.Allowed)
        {
            return CleanupOutcome.Failed(string.Join("; ", verdict.Reasons));
        }

        var isLarge = candidate.Size >= LargeFileThreshold;

        if (isLarge || !options.MoveToRecycleBin)
        {
            await _quarantine.QuarantineAsync(candidate.Path, options.QuarantineRetentionDays, ct);
            return CleanupOutcome.Success(CleanupStatus.Quarantined);
        }

        try
        {
            WindowsApi.MoveToRecycleBin(candidate.Path);
            return CleanupOutcome.Success(CleanupStatus.Recycled);
        }
        catch
        {
            await _quarantine.QuarantineAsync(candidate.Path, options.QuarantineRetentionDays, ct);
            return CleanupOutcome.Success(CleanupStatus.Quarantined);
        }
    }

    private sealed record CleanupOutcome(CleanupStatus Status, string Error)
    {
        public static CleanupOutcome Success(CleanupStatus status) => new(status, string.Empty);
        public static CleanupOutcome Failed(string error) => new(CleanupStatus.Failed, error);
    }
}
