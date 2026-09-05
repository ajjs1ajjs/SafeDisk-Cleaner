using SafeDiskCleaner.Core.Models;
using DriveInfo = SafeDiskCleaner.Core.Models.DriveInfo;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>
/// Drive listing for Unix (Linux/macOS) via System.IO.DriveInfo over
/// <see cref="Directory.GetLogicalDrives"/>. "Letter" is the mount point root.
/// </summary>
public sealed class UnixDriveService : IDriveService
{
    public IReadOnlyList<DriveInfo> ListDrives()
    {
        var result = new List<DriveInfo>();
        try
        {
            foreach (var root in Directory.GetLogicalDrives())
            {
                try
                {
                    var info = new System.IO.DriveInfo(root);
                    if (info.TotalSize <= 0)
                    {
                        continue;
                    }

                    var letter = info.Name.TrimEnd('/', '\\');
                    result.Add(new DriveInfo
                    {
                        Letter = string.IsNullOrEmpty(letter) ? "/" : letter,
                        Kind = KindOf(info.DriveType),
                        Total = (ulong)info.TotalSize,
                        Free = (ulong)info.AvailableFreeSpace,
                        Used = (ulong)info.TotalSize - (ulong)info.AvailableFreeSpace,
                    });
                }
                catch
                {
                    // volume became unavailable between enumeration and probe
                }
            }
        }
        catch
        {
            // no accessible volumes
        }

        return result;
    }

    private static string KindOf(DriveType type) => type switch
    {
        DriveType.Fixed => "fixed",
        DriveType.Removable => "removable",
        DriveType.Network => "remote",
        DriveType.CDRom => "cdrom",
        DriveType.Ram => "ramdisk",
        DriveType.NoRootDirectory => "no_root",
        _ => "unknown",
    };
}