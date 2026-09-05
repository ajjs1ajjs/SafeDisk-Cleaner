using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Windows;
using DriveInfo = SafeDiskCleaner.Core.Models.DriveInfo;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>Drive listing via the Win32 logical-drives APIs.</summary>
public sealed class WindowsDriveService : IDriveService
{
    public IReadOnlyList<DriveInfo> ListDrives() => WindowsApi.ListDrives();
}