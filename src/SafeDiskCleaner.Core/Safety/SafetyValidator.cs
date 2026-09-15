using SafeDiskCleaner.Core.Confidence;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Rules;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Core.Safety;

public sealed class SafetyVerdict
{
    public bool Allowed { get; private init; }
    public IReadOnlyList<string> Reasons { get; private init; } = Array.Empty<string>();

    public static SafetyVerdict Allow() => new() { Allowed = true, Reasons = Array.Empty<string>() };

    public static SafetyVerdict Deny(string reason) => new() { Allowed = false, Reasons = [reason] };
}

public sealed class SafetyValidator
{
    private const string RecycleBinSentinel = "__recycle_bin__";

    private readonly SignatureInspector _signatureInspector;

    public SafetyValidator(SignatureInspector signatureInspector)
    {
        _signatureInspector = signatureInspector;
    }

    public SafetyVerdict Validate(string path, Category category, uint recencyDays)
        => Validate(path, category, recencyDays, scanRoots: null);

    public SafetyVerdict Validate(
        string path, Category category, uint recencyDays, IReadOnlyList<string>? scanRoots)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(Path.GetFileName(path)))
        {
            return SafetyVerdict.Deny("Invalid path");
        }

        // Containment jail: the final path must still sit under one of the
        // scan roots that produced the candidate. `..`-escapes and UNC/`\\?\`
        // prefixes never pass.
        if (scanRoots is { Count: > 0 } && !IsWithinAnyRoot(path, scanRoots))
        {
            return SafetyVerdict.Deny("Path escapes the scan roots");
        }

        if (ClassificationEngine.IsProtectedExtension(path))
        {
            return SafetyVerdict.Deny($"Protected extension: {Path.GetExtension(path)}");
        }

        if (PathProtection.IsProtectedPath(path))
        {
            return SafetyVerdict.Deny("Path belongs to a protected system directory");
        }

        var lower = path.Replace('/', '\\').ToLowerInvariant();
        if (lower.Contains(@"\safedisk\quarantine", StringComparison.Ordinal)
            || lower.Contains(@"\safedisk\audit", StringComparison.Ordinal)
            || lower.Equals(RecycleBinSentinel, StringComparison.Ordinal))
        {
            return SafetyVerdict.Deny("Path is part of SafeDisk internals");
        }

        if (FileState.HasSystemAttribute(path))
        {
            return SafetyVerdict.Deny("File has the SYSTEM attribute");
        }

        if (FileState.AttributesUnreadable(path))
        {
            // Fail closed: an ACL-denied read between scan and cleanup must
            // not look like a clean file.
            return SafetyVerdict.Deny("File attributes unreadable");
        }

        try
        {
            // Windows may not update LastAccessTime (NTFS last-access is disabled
            // by default), so also consider LastWriteTime: a file written or
            // accessed recently must not be cleaned.
            var accessed = File.GetLastAccessTimeUtc(path);
            var written = File.GetLastWriteTimeUtc(path);
            var mostRecent = accessed > written ? accessed : written;
            var days = ConfidenceEngine.ElapsedDays(mostRecent);
            if (days < recencyDays)
            {
                return SafetyVerdict.Deny(
                    $"File was last used {days} day(s) ago (recency threshold {recencyDays} days)");
            }
        }
        catch (FileNotFoundException)
        {
            return SafetyVerdict.Deny("File no longer exists");
        }
        catch (DirectoryNotFoundException)
        {
            return SafetyVerdict.Deny("File no longer exists");
        }
        catch
        {
            // Vanished concurrently — deny rather than clean blindly.
            return SafetyVerdict.Deny("File metadata unavailable");
        }

        if (FileState.IsLocked(path))
        {
            return SafetyVerdict.Deny("File is open by another process");
        }

        if (FileState.LockCheckInconclusive(path))
        {
            return SafetyVerdict.Deny("File lock state unreadable");
        }

        if (category.RiskLevel() == RiskLevel.Advanced && _signatureInspector.HasMicrosoftSignature(path))
        {
            return SafetyVerdict.Deny("File carries a Microsoft digital signature");
        }

        return SafetyVerdict.Allow();
    }

    private static bool IsWithinAnyRoot(string path, IReadOnlyList<string> roots)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }
        var cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (var root in roots)
        {
            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }
            if (full.Equals(fullRoot, cmp)
                || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, cmp))
            {
                return true;
            }
        }
        return false;
    }
}
