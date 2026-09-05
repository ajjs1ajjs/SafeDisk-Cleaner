using SafeDiskCleaner.Core.Abstractions;

namespace SafeDiskCleaner.Infrastructure.Services;

public sealed class AppPaths : IAppPaths
{
    public string DataRoot { get; }
    public string AuditDir { get; }
    public string QuarantineDir { get; }
    public string ReportsDir { get; }

    public AppPaths()
    {
        DataRoot = ResolveDataRoot();
        AuditDir = Path.Combine(DataRoot, "audit");
        QuarantineDir = Path.Combine(DataRoot, "quarantine");
        ReportsDir = Path.Combine(DataRoot, "reports");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AuditDir);
        Directory.CreateDirectory(QuarantineDir);
        Directory.CreateDirectory(ReportsDir);
    }

    private static string ResolveDataRoot()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var primary = Path.Combine(programData, "SafeDisk");
        try
        {
            Directory.CreateDirectory(primary);
            return primary;
        }
        catch
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafeDisk");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
