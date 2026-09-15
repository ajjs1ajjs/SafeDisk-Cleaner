using FluentValidation;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Core.Validation;

public sealed class ScanOptionsValidator : AbstractValidator<ScanOptions>
{
    public ScanOptionsValidator()
    {
        RuleFor(x => x.MinConfidence)
            .Must(c => c <= 100)
            .WithMessage("Confidence threshold must be 0–100.");

        RuleFor(x => x.RecencyDays)
            .Must(d => d is >= 1 and <= 3650)
            .WithMessage("Recency window must be 1-3650 days (0 would drop the age floor).");

        RuleFor(x => x.Roots)
            .Must(roots => roots.All(r => !string.IsNullOrWhiteSpace(r)))
            .When(x => x.Roots.Count > 0)
            .WithMessage("Root paths must not be empty.");

        RuleForEach(x => x.Roots)
            .Must(BeScannableRoot)
            .When(x => x.Roots.Count > 0)
            .WithMessage("Scan root must be absolute, existing and not a reparse point.");
    }

    private static bool BeScannableRoot(string root) =>
        Scanning.Scanner.IsScannableRoot(root);
}

public sealed class CleanupOptionsValidator : AbstractValidator<CleanupOptions>
{
    public CleanupOptionsValidator()
    {
        RuleFor(x => x.QuarantineRetentionDays)
            .Must(d => d is >= 1 and <= 3650)
            .WithMessage("Quarantine retention must be 1–3650 days.");

        RuleFor(x => x.AutoThreshold)
            .Must(c => c is >= 80 and <= 100)
            .WithMessage("Auto threshold must be 80-100 (lower would auto-delete too eagerly).");

        RuleFor(x => x.RecencyDays)
            .Must(d => d is >= 1 and <= 3650)
            .WithMessage("Recency floor must be 1-3650 days.");
    }
}
