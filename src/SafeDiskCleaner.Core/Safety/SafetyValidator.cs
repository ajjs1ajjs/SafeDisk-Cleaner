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
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(Path.GetFileName(path)))
        {
            return SafetyVerdict.Deny("Invalid path");
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
            || path.Equals(RecycleBinSentinel, StringComparison.Ordinal))
        {
            return SafetyVerdict.Deny("Path is part of SafeDisk internals");
        }

        if (FileState.HasSystemAttribute(path))
        {
            return SafetyVerdict.Deny("File has the SYSTEM attribute");
        }

        try
        {
            var accessed = File.GetLastAccessTimeUtc(path);
            var days = ConfidenceEngine.ElapsedDays(accessed);
            if (days < recencyDays)
            {
                return SafetyVerdict.Deny(
                    $"File was accessed {days} day(s) ago (recency threshold {recencyDays} days)");
            }
        }
        catch
        {
            // Metadata unavailable — do not block on it, the file may have been
            // removed concurrently between scan and cleanup.
        }

        if (FileState.IsLocked(path))
        {
            return SafetyVerdict.Deny("File is open by another process");
        }

        if (category.RiskLevel() == RiskLevel.Advanced && _signatureInspector.HasMicrosoftSignature(path))
        {
            return SafetyVerdict.Deny("File carries a Microsoft digital signature");
        }

        return SafetyVerdict.Allow();
    }
}
