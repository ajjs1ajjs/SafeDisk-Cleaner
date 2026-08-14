using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Core.Rules;

public enum MatchKind
{
    None,
    Candidate,
    Protected,
}

public sealed record ClassificationResult(MatchKind Kind, Category? Category, byte BaseConfidence, string? Reason)
{
    public static ClassificationResult None() => new(MatchKind.None, null, 0, null);
    public static ClassificationResult Protected() => new(MatchKind.Protected, null, 0, null);
    public static ClassificationResult CandidateResult(Category category, byte baseConfidence, string reason) =>
        new(MatchKind.Candidate, category, baseConfidence, reason);
}

/// <summary>
/// File classification engine. Mirrors the original Rust rules: a file is
/// classified into a cleaning category with a base confidence score, or is
/// marked as protected (never cleanable).
/// </summary>
public static class ClassificationEngine
{
    private static readonly string[] ProtectedExtensions = ["dll", "sys", "exe", "cat", "inf", "msi", "msp"];

    public static bool IsProtectedExtension(string path) =>
        IsProtectedExtension((ReadOnlySpan<char>)Path.GetExtension(path));

    public static bool IsProtectedExtension(ReadOnlySpan<char> extension)
    {
        if (extension.IsEmpty)
        {
            return false;
        }

        Span<char> lower = stackalloc char[extension.Length];
        extension.ToLowerInvariant(lower);
        if (lower[0] == '.')
        {
            lower = lower[1..];
        }

        foreach (var ext in ProtectedExtensions)
        {
            if (lower.SequenceEqual(ext.AsSpan()))
            {
                return true;
            }
        }

        return false;
    }

    public static ClassificationResult Classify(string path)
    {
        if (IsProtectedExtension(path))
        {
            return ClassificationResult.Protected();
        }

        var lower = path.Replace('/', '\\').ToLowerInvariant();
        var name = Path.GetFileName(lower);

        if (name is "memdmp.dmp" or "memory.dmp")
        {
            return ClassificationResult.CandidateResult(Category.CrashDump, 97, "System memory crash dump");
        }

        if (lower.Contains(@"\crashdumps", StringComparison.Ordinal) || lower.Contains("crashpad", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.CrashDump, 95, "Crash dump directory");
        }

        if (string.Equals(Path.GetExtension(path), ".dmp", StringComparison.OrdinalIgnoreCase))
        {
            return ClassificationResult.CandidateResult(Category.CrashDump, 93, "Crash dump file");
        }

        var isFirefoxProfile = lower.Contains(@"\mozilla\firefox\profiles", StringComparison.Ordinal);
        if (isFirefoxProfile &&
            (lower.Contains("cache2", StringComparison.Ordinal) || lower.Contains("startupcache", StringComparison.Ordinal)))
        {
            return ClassificationResult.CandidateResult(Category.BrowserCache, 96, "Firefox cache");
        }

        var isChromium = lower.Contains(@"\google\chrome\user data", StringComparison.Ordinal)
            || lower.Contains(@"\microsoft\edge\user data", StringComparison.Ordinal)
            || lower.Contains(@"\chromium\user data", StringComparison.Ordinal);
        var isBrowserCacheDir = lower.Contains(@"\cache", StringComparison.Ordinal) || lower.Contains("code cache", StringComparison.Ordinal);
        if (isChromium && isBrowserCacheDir)
        {
            var engine = lower.Contains(@"\google\chrome\user data", StringComparison.Ordinal)
                ? "Chromium/Chrome"
                : lower.Contains(@"\microsoft\edge\user data", StringComparison.Ordinal)
                    ? "Edge"
                    : "Chromium";
            return ClassificationResult.CandidateResult(Category.BrowserCache, 97, $"{engine} browser cache");
        }

        if (lower.Contains(@"softwaredistribution\download", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.WindowsUpdateCache, 90, "Windows Update download cache");
        }

        if (IsPackageCachePath(lower))
        {
            return ClassificationResult.CandidateResult(Category.PackageCache, 92, "Package manager cache");
        }

        if (lower.Contains(@"\deliveryoptimization\", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.DeliveryOptimization, 88, "Delivery Optimization cache");
        }

        if (lower.Contains(@"\windows\wer\", StringComparison.Ordinal)
            || lower.Contains(@"\windows\werreportqueue", StringComparison.Ordinal)
            || lower.Contains(@"\wer\reportqueue", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.WindowsErrorReporting, 92, "Windows Error Reporting queue");
        }

        if (lower.Contains(@"\windows\inetcache", StringComparison.Ordinal)
            || lower.Contains(@"\internet explorer\cache", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.InternetCache, 93, "Internet Explorer cache");
        }

        if (lower.Contains(@"\windows\prefetch\", StringComparison.Ordinal)
            && name.EndsWith(".pf", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.Prefetch, 80, "Prefetch file");
        }

        // NOTE: the Windows DriverStore (C:\Windows\System32\DriverStore) is NOT
        // treated as cleanable cache — it holds installed driver packages. Removing
        // it here prevents offering installed drivers for deletion.

        if (lower.Contains(@"\windowstemp", StringComparison.Ordinal) || lower.Contains(@"\temp\", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.Temp, 99, "Temporary file");
        }

        if (string.Equals(Path.GetExtension(path), ".log", StringComparison.OrdinalIgnoreCase))
        {
            return ClassificationResult.CandidateResult(Category.Logs, 85, "Log file");
        }

        if (lower.Contains(@"\microsoft\windows\explorer", StringComparison.Ordinal) &&
            (name.StartsWith("thumbcache", StringComparison.Ordinal) || name.StartsWith("iconcache", StringComparison.Ordinal)))
        {
            return ClassificationResult.CandidateResult(Category.ThumbnailCache, 96, "Windows thumbnail cache database");
        }

        if (lower.Contains(@"\windows.old", StringComparison.Ordinal) || lower.Contains(@"\windows~old", StringComparison.Ordinal))
        {
            return ClassificationResult.CandidateResult(Category.OldWindowsInstall, 97, "File from a previous Windows installation");
        }

        return ClassificationResult.None();
    }

    private static bool IsPackageCachePath(string lower) =>
        lower.Contains(@"\nuget\cache", StringComparison.Ordinal)
        || lower.Contains(@"\npm-cache", StringComparison.Ordinal)
        || lower.Contains(@"\pnpm", StringComparison.Ordinal)
        || lower.Contains(@"\yarn\cache", StringComparison.Ordinal)
        || lower.Contains(@"\.yarn\berry", StringComparison.Ordinal)
        || lower.Contains(@"\pip\cache", StringComparison.Ordinal)
        || lower.Contains(@"\.bun\", StringComparison.Ordinal)
        || lower.Contains(@"\.cargo\registry\cache", StringComparison.Ordinal)
        || lower.Contains(@"\.gradle\caches", StringComparison.Ordinal)
        || lower.Contains("packagecache", StringComparison.Ordinal);
}
