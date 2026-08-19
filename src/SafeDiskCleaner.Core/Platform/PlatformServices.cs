namespace SafeDiskCleaner.Core.Platform;

/// <summary>Resolves the OS-appropriate implementations of platform services.</summary>
public static class PlatformServices
{
    public static IDriveService Drives { get; } =
        OperatingSystem.IsWindows() ? new WindowsDriveService() : new UnixDriveService();

    public static IRecycleBin RecycleBin { get; } =
        OperatingSystem.IsWindows() ? new WindowsRecycleBin() : new UnixRecycleBin();
}