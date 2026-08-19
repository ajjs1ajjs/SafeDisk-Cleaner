using SafeDiskCleaner.Core.Models;
using DriveInfo = SafeDiskCleaner.Core.Models.DriveInfo;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>
/// Lists storage volumes. Windows returns drive letters; Unix returns mount
/// points (/, /home, ...). Implemented per-OS so the rest of the app stays
/// platform-agnostic.
/// </summary>
public interface IDriveService
{
    IReadOnlyList<DriveInfo> ListDrives();
}