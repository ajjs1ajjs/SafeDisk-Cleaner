namespace SafeDiskCleaner.Core.Models;

public sealed class Candidate
{
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
    public Category Category { get; init; }
    public byte Confidence { get; init; }
    public CandidateAction Action { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? LastModified { get; init; }
    public uint? LastAccessDays { get; init; }
    public RiskLevel RiskLevel { get; init; }
}

public sealed class CategoryStats
{
    public Category Category { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public int Count { get; init; }
    public long Size { get; init; }
    public long Potential { get; init; }
}

public sealed class ScanSummary
{
    public ulong ScannedDirs { get; init; }
    public ulong ScannedFiles { get; init; }
    public long ElapsedMs { get; init; }
    public long TotalPotential { get; init; }
    public int TotalCandidates { get; init; }
    public IReadOnlyList<CategoryStats> Categories { get; init; } = Array.Empty<CategoryStats>();
}

public sealed class ScanResult
{
    public IReadOnlyList<Candidate> Candidates { get; init; } = Array.Empty<Candidate>();
    public ScanSummary Summary { get; init; } = new();
}

public sealed class ScanProgress
{
    public string CurrentRoot { get; init; } = string.Empty;
    public ulong FilesScanned { get; init; }
    public ulong DirsScanned { get; init; }
    public ulong CandidatesFound { get; init; }
    public double Percent { get; init; }
    public bool Finished { get; init; }
}

public sealed class CleanupProgress
{
    public ulong Processed { get; init; }
    public ulong Total { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public double Percent { get; init; }
    public bool Finished { get; init; }
}

public sealed class CleanupEntry
{
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
    public Category Category { get; init; }
    public byte Confidence { get; init; }
    public CleanupStatus Status { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class CleanupResult
{
    public CleanMode Mode { get; init; }
    public int Processed { get; init; }
    public int Deleted { get; init; }
    public long FreedBytes { get; init; }
    public IReadOnlyList<CleanupEntry> Entries { get; init; } = Array.Empty<CleanupEntry>();
}

public sealed class AuditEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
    public bool Success { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class QuarantineEntry
{
    public string Id { get; init; } = string.Empty;
    public string OriginalPath { get; init; } = string.Empty;
    public string QuarantinedPath { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTimeOffset QuarantinedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class DriveInfo
{
    public string Letter { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public ulong Total { get; init; }
    public ulong Free { get; init; }
    public ulong Used { get; init; }
}

public sealed class UpdateInfo
{
    public bool Available { get; init; }
    public string LatestVersion { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
}

public sealed class ScanOptions
{
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public bool IncludeMedium { get; init; }
    public bool IncludeAdvanced { get; init; }
    public byte MinConfidence { get; init; } = 50;
    public uint RecencyDays { get; init; } = 7;
}

public sealed class CleanupOptions
{
    public CleanMode Mode { get; init; } = CleanMode.Interactive;
    public uint QuarantineRetentionDays { get; init; } = 14;
    public bool MoveToRecycleBin { get; init; } = true;
    public byte AutoThreshold { get; init; } = 95;
}
