namespace SafeDiskCleaner.Infrastructure.Data;

public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool Success { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class QuarantineEntity
{
    public string Id { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string StoredName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset QuarantinedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
