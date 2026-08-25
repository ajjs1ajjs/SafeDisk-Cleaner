using System.Diagnostics;
using System.Text;
using SafeDiskCleaner.Core.Abstractions;

namespace SafeDiskCleaner.Core.Platform;

/// <summary>
/// OS-level auto-clean scheduling: schtasks on Windows, a systemd user timer
/// on Linux and a launchd agent on macOS. The platform-specific payloads are
/// produced by pure static builders (unit-tested); the service only writes
/// files / shells out to the OS tools.
/// </summary>
public sealed class ScheduleService : IScheduleService
{
    public const string TaskName = "SafeDiskCleanerAutoClean";
    private const string LinuxServiceName = "safedisk-autoclean";

    public bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    // ---------- Windows (schtasks) ----------

    /// <summary>Builds the argument list for `schtasks /Create`.</summary>
    public static string BuildSchTasksCreateArgs(ScheduleOptions options)
    {
        var schedule = options.Frequency == ScheduleFrequency.Weekly
            ? $"/SC WEEKLY /D {DayOfWeekAbbrev(DateTime.Today.DayOfWeek)}"
            : "/SC DAILY";
        return $"/Create /F /TN {TaskName} /TR \"{options.ExecutablePath} {options.Arguments}\" {schedule} /ST {NormalizeTime(options.TimeOfDay)}";
    }

    /// <summary>schtasks weekday abbreviations (MON..SUN).</summary>
    private static string DayOfWeekAbbrev(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        _ => "SUN",
    };

    /// <summary>Builds the argument list for `schtasks /Delete`.</summary>
    public static string BuildSchTasksDeleteArgs() => $"/Delete /F /TN {TaskName}";

    /// <summary>Builds the argument list for the existence probe.</summary>
    public static string BuildSchTasksQueryArgs() => $"/Query /TN {TaskName}";

    // ---------- Linux (systemd user units) ----------

    public static string BuildSystemdServiceUnit(string executablePath, string arguments) => $"""
        [Unit]
        Description=SafeDisk Cleaner automatic cleanup

        [Service]
        Type=oneshot
        ExecStart={executablePath} {arguments}
        """;

    public static string BuildSystemdTimerUnit(string timeOfDay) => $"""
        [Unit]
        Description=Runs SafeDisk Cleaner automatic cleanup

        [Timer]
        OnCalendar=*-*-* {NormalizeTime(timeOfDay)}:00
        Persistent=true

        [Install]
        WantedBy=timers.target
        """;

    // ---------- macOS (launchd agent plist) ----------

    public static string BuildLaunchdPlist(string label, string executablePath, string[] arguments, int hour, int minute)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");
        sb.AppendLine("    <key>Label</key>");
        sb.AppendLine($"    <string>{label}</string>");
        sb.AppendLine("    <key>ProgramArguments</key>");
        sb.AppendLine("    <array>");
        sb.AppendLine($"        <string>{executablePath}</string>");
        foreach (var argument in arguments)
        {
            sb.AppendLine($"        <string>{argument}</string>");
        }

        sb.AppendLine("    </array>");
        sb.AppendLine("    <key>StartCalendarInterval</key>");
        sb.AppendLine("    <dict>");
        sb.AppendLine("        <key>Hour</key>");
        sb.AppendLine($"        <integer>{hour}</integer>");
        sb.AppendLine("        <key>Minute</key>");
        sb.AppendLine($"        <integer>{minute}</integer>");
        sb.AppendLine("    </dict>");
        sb.AppendLine("</dict>");
        sb.Append("</plist>");
        return sb.ToString();
    }

    // ---------- IScheduleService ----------

    public async Task<bool> IsRegisteredAsync()
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return await RunQuiet("schtasks", BuildSchTasksQueryArgs());
            }

            if (OperatingSystem.IsLinux())
            {
                return await RunQuiet("systemctl", $"--user is-enabled --quiet {LinuxServiceName}.timer");
            }

            if (OperatingSystem.IsMacOS())
            {
                return await RunQuiet("launchctl", $"list {LinuxServiceName}");
            }
        }
        catch
        {
            // tooling missing — treat as not registered
        }

        return false;
    }

    public async Task ApplyAsync(ScheduleOptions options)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Scheduling is not supported on this platform");
        }

        if (OperatingSystem.IsWindows())
        {
            var result = await RunProcess("schtasks", BuildSchTasksCreateArgs(options));
            if (result != 0)
            {
                throw new InvalidOperationException($"schtasks failed with exit code {result}");
            }

            return;
        }

        if (OperatingSystem.IsLinux())
        {
            WriteUserFile($"{LinuxServiceName}.service", BuildSystemdServiceUnit(options.ExecutablePath, options.Arguments));
            WriteUserFile($"{LinuxServiceName}.timer", BuildSystemdTimerUnit(options.TimeOfDay));
            _ = await RunProcess("systemctl", "--user daemon-reload");
            var rc = await RunProcess("systemctl", $"--user enable --now {LinuxServiceName}.timer");
            if (rc != 0)
            {
                throw new InvalidOperationException($"systemctl enable failed with exit code {rc}");
            }

            return;
        }

        // macOS launchd agent
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var agentsDir = Path.Combine(home, "Library", "LaunchAgents");
        Directory.CreateDirectory(agentsDir);
        var plistPath = Path.Combine(agentsDir, $"{TaskName}.plist");
        var parts = SplitArguments(options.Arguments);
        var hour = int.Parse(NormalizeTime(options.TimeOfDay)[..2], System.Globalization.CultureInfo.InvariantCulture);
        var minute = int.Parse(NormalizeTime(options.TimeOfDay)[3..5], System.Globalization.CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(plistPath, BuildLaunchdPlist(TaskName, options.ExecutablePath, [.. parts], hour, minute));
        _ = await RunProcess("launchctl", $"unload {plistPath}");
        var loadRc = await RunProcess("launchctl", $"load {plistPath}");
        if (loadRc != 0)
        {
            throw new InvalidOperationException($"launchctl load failed with exit code {loadRc}");
        }
    }

    public async Task RemoveAsync()
    {
        if (!await IsRegisteredAsync())
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                _ = await RunProcess("schtasks", BuildSchTasksDeleteArgs());
            }
            else if (OperatingSystem.IsLinux())
            {
                _ = await RunProcess("systemctl", $"--user disable --now {LinuxServiceName}.timer");
                DeleteUserFile($"{LinuxServiceName}.timer");
                DeleteUserFile($"{LinuxServiceName}.service");
                _ = await RunProcess("systemctl", "--user daemon-reload");
            }
            else if (OperatingSystem.IsMacOS())
            {
                var plistPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "LaunchAgents", $"{TaskName}.plist");
                _ = await RunProcess("launchctl", $"unload {plistPath}");
                if (File.Exists(plistPath))
                {
                    File.Delete(plistPath);
                }
            }
        }
        catch
        {
            // removal is best-effort
        }
    }

    // ---------- helpers ----------

    internal static string NormalizeTime(string timeOfDay)
    {
        // Accepts both strict "HH:mm" and lenient forms ("3:5", "4am"-free variants).
        if (TimeSpan.TryParse(timeOfDay, System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            value < TimeSpan.FromDays(1))
        {
            return new TimeSpan(value.Hours, value.Minutes, 0).ToString(@"hh\:mm");
        }

        return "03:00";
    }

    internal static string[] SplitArguments(string arguments) =>
        arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<int> RunProcess(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null)
        {
            return -1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task<bool> RunQuiet(string fileName, string arguments) =>
        await RunProcess(fileName, arguments) == 0;

    private static string GetUserUnitDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "systemd", "user");

    private static void WriteUserFile(string fileName, string content)
    {
        var dir = GetUserUnitDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content, new UTF8Encoding(false));
    }

    private static void DeleteUserFile(string fileName)
    {
        var path = Path.Combine(GetUserUnitDir(), fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
