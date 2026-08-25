using System.Diagnostics;
using System.Runtime.Versioning;

namespace SafeDiskCleaner.Core.Windows;

/// <summary>An installed application discovered in the Windows uninstall registry.</summary>
public sealed record InstalledApp(
    string Name,
    string Version,
    string Publisher,
    long EstimatedSizeKb,
    string UninstallString,
    string QuietUninstallString);

/// <summary>
/// Reads installed applications from the Windows "Uninstall" registry views
/// (HKLM 64/32 + HKCU). Returns an empty list on non-Windows platforms.
/// </summary>
public static class InstalledAppsReader
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<InstalledApp> ListInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<InstalledApp>();
        }

        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        CollectFrom(Microsoft.Win32.Registry.LocalMachine, $@"{UninstallKeyPath}", apps);
        CollectFrom(Microsoft.Win32.Registry.LocalMachine, $@"WOW6432Node\{UninstallKeyPath}", apps);
        CollectFrom(Microsoft.Win32.Registry.CurrentUser, UninstallKeyPath, apps);

        return apps.Values
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.UninstallString))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [SupportedOSPlatform("windows")]
    private static void CollectFrom(Microsoft.Win32.RegistryKey hive, string path, Dictionary<string, InstalledApp> apps)
    {
        try
        {
            using var root = hive.OpenSubKey(path);
            if (root is null)
            {
                return;
            }

            foreach (var keyName in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(keyName);
                    if (key is null)
                    {
                        continue;
                    }

                    // system components are not user-uninstallable
                    if ((key.GetValue("SystemComponent") as int?) == 1)
                    {
                        continue;
                    }

                    var name = key.GetValue("DisplayName") as string;
                    var uninstall = key.GetValue("UninstallString") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uninstall))
                    {
                        continue;
                    }

                    var entry = new InstalledApp(
                        Name: name.Trim(),
                        Version: (key.GetValue("DisplayVersion") as string)?.Trim() ?? string.Empty,
                        Publisher: (key.GetValue("Publisher") as string)?.Trim() ?? string.Empty,
                        EstimatedSizeKb: (key.GetValue("EstimatedSize") as int?) ?? 0,
                        UninstallString: uninstall.Trim(),
                        QuietUninstallString: (key.GetValue("QuietUninstallString") as string)?.Trim() ?? string.Empty);

                    apps[name.Trim()] = entry; // 64-bit view wins over 32-bit duplicate
                }
                catch
                {
                    // unreadable/unexpected key — skip
                }
            }
        }
        catch
        {
            // missing hive view — skip
        }
    }

    /// <summary>
    /// Splits an uninstall command line into executable + arguments, honoring
    /// quotes. Returns false when nothing sensible can be extracted.
    /// </summary>
    public static bool TrySplitCommand(string commandLine, out string executable, out string arguments)
    {
        executable = string.Empty;
        arguments = string.Empty;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
            {
                return false;
            }

            executable = trimmed[1..endQuote];
            arguments = trimmed[(endQuote + 1)..].Trim();
            return executable.Length > 0;
        }

        // MsiExec-style and unquoted paths: split at ".exe " boundary when present
        var exeEnd = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd >= 0)
        {
            exeEnd += 4;
            executable = trimmed[..exeEnd];
            arguments = trimmed[exeEnd..].TrimStart();
            return true;
        }

        var space = trimmed.IndexOf(' ');
        if (space < 0)
        {
            executable = trimmed;
            return File.Exists(trimmed);
        }

        executable = trimmed[..space];
        arguments = trimmed[(space + 1)..].Trim();
        return executable.Length > 0 && File.Exists(executable);
    }

    /// <summary>Parses "HH:mm"; falls back to null when malformed.</summary>
    public static bool TryParseTime(string? text, out int hour, out int minute)
    {
        hour = 3;
        minute = 0;
        if (TimeSpan.TryParseExact(text, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            hour = value.Hours;
            minute = value.Minutes;
            return true;
        }

        return false;
    }
}
