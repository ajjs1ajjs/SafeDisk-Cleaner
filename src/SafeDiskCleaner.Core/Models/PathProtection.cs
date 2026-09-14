namespace SafeDiskCleaner.Core.Models;

public static class PathProtection
{
    private static readonly string[] ProtectedNeedles =
    [
        @"\windows\",
        @"c:\windows\",
        @"c:\program files",
        @"c:\program files (x86)",
        @"c:\programdata",
        @"\system32",
        @"\syswow64",
        @"\drivers\",
        @"\efi\",
        @"\recovery\",
        @"\boot\",
        "$recycle.bin",
        @"\system volume information",
    ];

    // NOTE: candidates are normalized, so macOS needles use backslashes too.
    internal static readonly string[] MacOsProtectedNeedles =
    [
        @"\system\",
        @"\usr\",
        @"\bin\",
        @"\sbin\",
        @"\etc\",
        @"\boot\",
        @"\library\",
    ];

    // Windows.old / Windows~old are scan roots: their nested \Windows\, System32,
    // etc. belong to the OLD install that is junk to clean, so the system needles
    // must not prune it. Only hard safety needles still apply.
    private static readonly string[] WindowsOldProtectedNeedles =
    [
        @"\recovery\",
        "$recycle.bin",
        @"\system volume information",
    ];

    private static readonly string[] WindowsOldMarkers = ["windows.old", "windows~old"];

    /// <summary>
    /// Determines whether a path belongs to a protected system directory.
    /// Uses a best-effort canonicalization first so that ".." segments and
    /// relative paths cannot bypass the check.
    /// </summary>
    public static bool IsProtectedPath(string path) => IsProtectedPath(path, OperatingSystem.IsMacOS());

    internal static bool IsProtectedPath(string path, bool isMacOS)
    {
        var candidates = new List<string>
        {
            Normalize(path),
        };

        try
        {
            var full = Path.GetFullPath(path);
            candidates.Add(Normalize(full));
            var canonical = new DirectoryInfo(full).FullName;
            candidates.Add(Normalize(canonical));
        }
        catch
        {
            // ignore — the raw form is still checked below
        }

        foreach (var candidate in candidates)
        {
            var inWindowsOld = WindowsOldMarkers.Any(candidate.Contains);
            var needles = inWindowsOld ? WindowsOldProtectedNeedles : ProtectedNeedles;
            if (needles.Any(candidate.Contains))
            {
                return true;
            }
        }

        if (isMacOS)
        {
            foreach (var candidate in candidates)
            {
                if (MacOsProtectedNeedles.Any(n => ContainsNeedle(candidate, n)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsNeedle(string candidate, string needle) =>
        candidate.Contains(needle, StringComparison.Ordinal)
        || candidate.Equals(needle.TrimEnd('\\'), StringComparison.Ordinal)
        || candidate.EndsWith(needle.TrimEnd('\\'), StringComparison.Ordinal);

    internal static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
