namespace SafeDiskCleaner.Core.Models;

public enum Category
{
    Temp,
    CrashDump,
    BrowserCache,
    AppCache,
    RecycleBin,
    Logs,
    WindowsUpdateCache,
    DriverCache,
    PackageCache,
    DuplicateFiles,
    LargeUnusedFiles,
    ThumbnailCache,
    OldWindowsInstall,
    DeliveryOptimization,
    WindowsErrorReporting,
    InternetCache,
    Prefetch,
}

public static class CategoryExtensions
{
    public static string Label(this Category category) => category switch
    {
        Category.Temp => "Temp files",
        Category.CrashDump => "Crash dumps",
        Category.BrowserCache => "Browser cache",
        Category.AppCache => "App cache",
        Category.RecycleBin => "Recycle Bin",
        Category.Logs => "Logs",
        Category.WindowsUpdateCache => "Windows Update cache",
        Category.DriverCache => "Driver cache",
        Category.PackageCache => "Package cache",
        Category.DuplicateFiles => "Duplicate files",
        Category.LargeUnusedFiles => "Large unused files",
        Category.ThumbnailCache => "Thumbnail cache",
        Category.OldWindowsInstall => "Old Windows installation",
        Category.DeliveryOptimization => "Delivery Optimization cache",
        Category.WindowsErrorReporting => "Windows Error Reporting",
        Category.InternetCache => "Internet cache",
        Category.Prefetch => "Prefetch files",
        _ => category.ToString(),
    };

    public static RiskLevel RiskLevel(this Category category) => category switch
    {
        Category.Temp or Category.CrashDump or Category.BrowserCache or Category.AppCache or Category.RecycleBin
            or Category.Logs or Category.ThumbnailCache or Category.DeliveryOptimization
            or Category.WindowsErrorReporting or Category.InternetCache => global::SafeDiskCleaner.Core.Models.RiskLevel.Safe,
        Category.WindowsUpdateCache or Category.DriverCache or Category.PackageCache
            or Category.Prefetch => global::SafeDiskCleaner.Core.Models.RiskLevel.Medium,
        _ => global::SafeDiskCleaner.Core.Models.RiskLevel.Advanced,
    };
}
