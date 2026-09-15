using System.Runtime.InteropServices;

namespace SafeDiskCleaner.Core.Models;

public static class PathProtection
{
    /// <summary>
    /// Determines whether a path belongs to a protected system directory.
    /// Matching is segment-anchored (never substring): needles match whole
    /// path segments, so `C:\Windows` itself is protected and `windows.old`
    /// nested deep inside `System32` does not disarm the system rules.
    /// </summary>
    public static bool IsProtectedPath(string path) => IsProtectedPath(path, OperatingSystem.IsMacOS());

    internal static bool IsProtectedPath(string path, bool isMacOS)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var segs = SplitSegments(path);
        if (segs.Length == 0)
            return true;

        // ADS / alternate-stream suffix: never cleanable (extension spoofing).
        if (HasAdsSuffix(path))
            return true;

        // Drive root itself (C:\, /) is always protected.
        if (IsDriveRoot(segs, path))
            return true;

        // windows.old exception applies ONLY as a first-level directory of a
        // drive root (C:\Windows.old\...). Nested deeper it means nothing.
        bool inWindowsOldRoot = IsWindowsOldRoot(segs);

        if (IsWindowsProtected(segs, inWindowsOldRoot))
            return true;

        if (isMacOS && IsMacOsProtected(segs))
            return true;

        return IsUnixProtected(segs);
    }

    // ---- segmentation & normalization ----

    internal static string[] SplitSegments(string path)
    {
        var expanded = ExpandShortNames(path);
        // Unix-absolute paths stay Unix-style even when evaluated on Windows
        // (tests + cross-OS catalog entries): GetFullPath would prepend a
        // drive letter and corrupt the segmentation.
        bool unixStyle = expanded.StartsWith('/');
        string full;
        if (unixStyle)
        {
            full = expanded;
        }
        else
        {
            try
            {
                full = Path.GetFullPath(expanded);
            }
            catch
            {
                full = expanded;
            }
        }
        var norm = full.Replace('/', '\\');
        // Lexical `..` resolution as a backup: GetFullPath does not resolve
        // foreign-style paths (e.g. Windows paths evaluated on Linux CI).
        var stack = new List<string>();
        foreach (var raw in norm.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = OperatingSystem.IsWindows() ? raw.TrimEnd('.', ' ') : raw;
            if (s.Length == 0 || s == ".")
                continue;
            if (s == "..")
            {
                if (stack.Count > 0 && !(stack.Count == 1 && stack[0].Length == 2 && stack[0][1] == ':'))
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(s);
        }
        return stack.ToArray();
    }

    internal static bool HasAdsSuffix(string path)
    {
        // Drive-letter colon is at index 1 (C:\...); any other colon starts
        // an ADS/stream suffix on Windows. UNC `\\?\` prefixes are rejected
        // outright (they bypass Win32 normalization).
        if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
            return true;
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] != ':')
                continue;
            if (i == 1 && path.Length > 2 && (path[2] == '\\' || path[2] == '/'))
                continue; // drive letter
            return true;
        }
        return false;
    }

    private static bool IsDriveRoot(string[] segs, string path)
    {
        if (segs.Length == 0)
            return true;
        // "C:" alone, or a single segment on Unix ("/x" is not a root; "/" is).
        if (OperatingSystem.IsWindows())
            return segs.Length == 1 && segs[0].Length == 2 && segs[0][1] == ':';
        return path.Trim() is "/" || (segs.Length == 0);
    }

    private static bool IsWindowsOldRoot(string[] segs)
    {
        // First real segment (after drive letter) equals windows.old.
        int first = segs.Length > 0 && segs[0].Length == 2 && segs[0][1] == ':' ? 1 : 0;
        return segs.Length > first
            && (segs[first].Equals("windows.old", StringComparison.OrdinalIgnoreCase)
                || segs[first].Equals("windows~old", StringComparison.OrdinalIgnoreCase));
    }

    // ---- per-OS rules ----

    private static bool IsWindowsProtected(string[] segs, bool inWindowsOldRoot)
    {
        int i = segs.Length > 0 && segs[0].Length == 2 && segs[0][1] == ':' ? 1 : 0;
        var rest = segs.Skip(i).Select(s => s.ToLowerInvariant()).ToArray();
        if (rest.Length == 0)
            return true;

        // Inside C:\Windows.old, only the hard safety needles apply.
        if (inWindowsOldRoot)
        {
            return rest.Skip(1).Any(s =>
                s is "recovery" or "$recycle.bin" or "system volume information");
        }

        string first = rest[0];
        // 8.3 short-name alias at drive level (PROGRA~1, DOCUME~1, WINDOWS~1…):
        // GetLongPathName expansion fails for nonexistent paths or volumes
        // with short names disabled, so an unexpanded alias must fail closed
        // here rather than slip past the needles below. Manual scan, no regex.
        int tilde = first.LastIndexOf('~');
        if (tilde > 0 && tilde < first.Length - 1)
        {
            bool allDigits = true;
            for (int k = tilde + 1; k < first.Length; k++)
            {
                if (!char.IsDigit(first[k])) { allDigits = false; break; }
            }
            if (allDigits)
                return true;
        }
        // Any-drive system locations (C:, D:, ...).
        if (first is "windows" or "program files" or "program files (x86)" or "programdata")
            return true;
        // User profile root itself (C:\Users\<name>): contents are scanned by
        // explicit sub-roots, never by profile-root identity.
        if (first == "users" && rest.Length == 2)
            return true;
        if (first == "users" && rest.Length > 2)
        {
            // NTUSER.DAT and friends at profile root are load-bearing.
            if (rest.Length == 3 && rest[2].StartsWith("ntuser.", StringComparison.Ordinal))
                return true;
        }
        // Boot / recovery / EFI anywhere in the first two levels.
        if (rest.Take(2).Any(s => s is "efi" or "boot" or "recovery" or "drivers"))
            return true;
        if (rest.Any(s => s is "system32" or "syswow64" or "system volume information" or "$recycle.bin"))
            return true;
        return false;
    }

    private static bool IsMacOsProtected(string[] segs)
    {
        var lower = segs.Select(s => s.ToLowerInvariant()).ToArray();
        if (lower.Length == 0)
            return true;
        if (lower[0] is "system" or "usr" or "bin" or "sbin" or "etc"
            or "private" or "cores" or "boot" or "opt")
            return true;
        if (lower[0] == "library")
            return true; // any depth: LaunchAgents/Daemons included
        if (lower[0] == "users" && lower.Length == 2)
            return true; // profile root itself
        return false;
    }

    private static bool IsUnixProtected(string[] segs)
    {
        var lower = segs.Select(s => s.ToLowerInvariant()).ToArray();
        if (lower.Length == 0)
            return true;
        // Bare system locations are always protected.
        if (lower.Length == 1)
            return true;
        // Profile roots themselves, never their contents (scanned via roots).
        if ((lower[0] is "home" or "root") && lower.Length == 2)
            return true;
        return false;
    }

    // ---- 8.3 short-name expansion (Windows) ----

    private static string ExpandShortNames(string path)
    {
        if (!OperatingSystem.IsWindows() || !path.Contains('~'))
            return path;
        try
        {
            var sb = new System.Text.StringBuilder(512);
            uint len = GetLongPathName(path, sb, (uint)sb.Capacity);
            if (len > 0 && len < 32768)
            {
                if (len >= sb.Capacity)
                {
                    sb.Capacity = (int)len + 1;
                    len = GetLongPathName(path, sb, (uint)sb.Capacity);
                }
                if (len > 0)
                    return sb.ToString();
            }
        }
        catch
        {
            // ignore — fall through to the unexpanded form, plus short-name
            // needles below as a second net
        }
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string lpszShortPath, System.Text.StringBuilder lpszLongPath, uint cchBuffer);

    internal static string Normalize(string path) => string.Join('\\', SplitSegments(path)).ToLowerInvariant();
}
