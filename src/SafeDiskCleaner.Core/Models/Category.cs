namespace SafeDiskCleaner.Core.Models;

public enum Category
{
    Temp,
    CrashDump,
    BrowserCache,
    RecycleBin,
    Logs,
    WindowsUpdateCache,
    DriverCache,
    PackageCache,
    DuplicateFiles,
    LargeUnusedFiles,
    ThumbnailCache,
    OldWindowsInstall,
}

public static class CategoryExtensions
{
    public static string Label(this Category category) => category switch
    {
        Category.Temp => "Temp files",
        Category.CrashDump => "Crash dumps",
        Category.BrowserCache => "Browser cache",
        Category.RecycleBin => "Recycle Bin",
        Category.Logs => "Logs",
        Category.WindowsUpdateCache => "Windows Update cache",
        Category.DriverCache => "Driver cache",
        Category.PackageCache => "Package cache",
        Category.DuplicateFiles => "Duplicate files",
        Category.LargeUnusedFiles => "Large unused files",
        Category.ThumbnailCache => "Thumbnail cache",
        Category.OldWindowsInstall => "Old Windows installation",
        _ => category.ToString(),
    };

    public static RiskLevel RiskLevel(this Category category) => category switch
    {
        Category.Temp or Category.CrashDump or Category.BrowserCache or Category.RecycleBin
            or Category.Logs or Category.ThumbnailCache => global::SafeDiskCleaner.Core.Models.RiskLevel.Safe,
        Category.WindowsUpdateCache or Category.DriverCache or Category.PackageCache => global::SafeDiskCleaner.Core.Models.RiskLevel.Medium,
        _ => global::SafeDiskCleaner.Core.Models.RiskLevel.Advanced,
    };
}
