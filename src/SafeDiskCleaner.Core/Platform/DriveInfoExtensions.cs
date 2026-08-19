using DriveInfo = SafeDiskCleaner.Core.Models.DriveInfo;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>Platform-aware helpers for converting drive entries to filesystem roots.</summary>
public static class DriveInfoExtensions
{
    /// <summary>
    /// Returns the filesystem root for a drive entry: "C:\" on Windows,
    /// "/" or "/home" on Unix (mount point roots are already absolute).
    /// </summary>
    public static string RootPath(this DriveInfo drive) =>
        OperatingSystem.IsWindows()
            ? $"{drive.Letter}\\"   // "C:\"
            : drive.Letter.StartsWith('/') ? drive.Letter : $"/{drive.Letter}";

    /// <summary>Builds a filesystem root from a drive letter/name string.</summary>
    public static string DriveRoot(string letter) =>
        OperatingSystem.IsWindows()
            ? $"{letter}\\"   // "C:\"
            : letter.StartsWith('/') ? letter : $"/{letter}";
}