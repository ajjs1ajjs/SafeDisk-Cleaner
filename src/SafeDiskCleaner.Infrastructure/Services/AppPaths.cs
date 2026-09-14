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
        if (OperatingSystem.IsMacOS())
        {
            var primary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "SafeDisk");
            try
            {
                Directory.CreateDirectory(primary);
                return primary;
            }
            catch
            {
                // fall through to LocalApplicationData (~/.local/share on macOS)
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // No root-owned /usr/share attempt: regular users store data under ~/.local/share.
            var linuxRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafeDisk");
            Directory.CreateDirectory(linuxRoot);
            return linuxRoot;
        }
        else
        {
            try
            {
                var programData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SafeDisk");
                Directory.CreateDirectory(programData);
                return programData;
            }
            catch
            {
                // fall through to per-user fallback below
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeDisk");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
