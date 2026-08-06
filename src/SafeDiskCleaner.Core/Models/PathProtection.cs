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
        @"$recycle.bin",
        @"\system volume information",
    ];

    /// <summary>
    /// Determines whether a path belongs to a protected system directory.
    /// Uses a best-effort canonicalization first so that ".." segments and
    /// relative paths cannot bypass the check.
    /// </summary>
    public static bool IsProtectedPath(string path)
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
            if (ProtectedNeedles.Any(candidate.Contains))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
