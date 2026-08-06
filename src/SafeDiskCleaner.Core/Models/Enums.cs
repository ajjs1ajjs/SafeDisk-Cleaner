namespace SafeDiskCleaner.Core.Models;

public enum CleanMode
{
    Analyze,
    DryRun,
    Interactive,
    Auto,
}

public enum CandidateAction
{
    Delete,
    Review,
    Keep,
}

public enum CleanupStatus
{
    Deleted,
    Quarantined,
    Recycled,
    Skipped,
    Failed,
    WouldDelete,
}

public static class CleanupStatusExtensions
{
    public static string AsString(this CleanupStatus status) => status switch
    {
        CleanupStatus.Deleted => "deleted",
        CleanupStatus.Quarantined => "quarantined",
        CleanupStatus.Recycled => "recycled",
        CleanupStatus.Skipped => "skipped",
        CleanupStatus.Failed => "failed",
        CleanupStatus.WouldDelete => "would_delete",
        _ => status.ToString(),
    };

    public static string Description(this CleanupStatus status) => status switch
    {
        CleanupStatus.Deleted => "Deleted permanently",
        CleanupStatus.Quarantined => "Moved to SafeDisk quarantine",
        CleanupStatus.Recycled => "Moved to Recycle Bin",
        CleanupStatus.Skipped => "Skipped",
        CleanupStatus.Failed => "Failed",
        CleanupStatus.WouldDelete => "Would delete",
        _ => status.ToString(),
    };

    public static bool IsSuccess(this CleanupStatus status) =>
        status is CleanupStatus.Deleted or CleanupStatus.Quarantined or CleanupStatus.Recycled;
}
