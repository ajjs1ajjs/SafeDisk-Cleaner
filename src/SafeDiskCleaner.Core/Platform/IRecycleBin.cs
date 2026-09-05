using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>
/// Platform abstraction over the OS recycle bin / trash. On platforms without
/// a trash concept the implementation degrades to no-ops, and callers fall
/// back to quarantine.
/// </summary>
public interface IRecycleBin
{
    /// <summary>Total size and item count currently in the trash, or null when unavailable.</summary>
    RecycleBinInfo? Query(string? root = null);

    /// <summary>Permanently empties the trash. Returns false when unsupported.</summary>
    bool Empty();

    /// <summary>Moves a path to the trash. Throws when unsupported (callers quarantine).</summary>
    void MoveToRecycleBin(string path);
}