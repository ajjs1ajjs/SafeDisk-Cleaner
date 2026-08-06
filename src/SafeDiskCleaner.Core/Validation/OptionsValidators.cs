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
            .Must(d => d <= 3650)
            .WithMessage("Recency window is out of range.");

        RuleFor(x => x.Roots)
            .Must(roots => roots.All(r => !string.IsNullOrWhiteSpace(r)))
            .When(x => x.Roots.Count > 0)
            .WithMessage("Root paths must not be empty.");
    }
}

public sealed class CleanupOptionsValidator : AbstractValidator<CleanupOptions>
{
    public CleanupOptionsValidator()
    {
        RuleFor(x => x.QuarantineRetentionDays)
            .Must(d => d is >= 1 and <= 3650)
            .WithMessage("Quarantine retention must be 1–3650 days.");

        RuleFor(x => x.AutoThreshold)
            .Must(c => c <= 100)
            .WithMessage("Auto threshold must be 0–100.");
    }
}
