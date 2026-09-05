using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Core.Confidence;

public sealed class ConfidenceInput
{
    public required byte Base { get; init; }
    public required Category Category { get; init; }
    public required long Size { get; init; }
    public DateTimeOffset? LastAccess { get; init; }
    public required uint RecencyDays { get; init; }
    public required bool Locked { get; init; }
    public required bool SystemAttr { get; init; }
}

public static class ConfidenceEngine
{
    private const long GiB = 1L << 30;
    private const long HalfGiB = 512L << 20;

    public static byte Compute(ConfidenceInput input)
    {
        var score = (int)input.Base;

        if (input.LastAccess is { } access)
        {
            var days = ElapsedDays(access);
            if (days < input.RecencyDays)
            {
                score -= 40;
            }
            else
            {
                score += (int)(Math.Min(days, 365) / 15);
            }
        }

        if (input.Locked)
        {
            score -= 60;
        }

        if (input.SystemAttr)
        {
            score -= 70;
        }

        switch (input.Category.RiskLevel())
        {
            case RiskLevel.Safe:
                break;
            case RiskLevel.Medium:
                score -= 8;
                break;
            case RiskLevel.Advanced:
                score -= 15;
                break;
        }

        if (input.Size >= GiB)
        {
            score -= 5;
        }
        else if (input.Size >= HalfGiB)
        {
            score -= 2;
        }

        return (byte)Math.Clamp(score, 0, 100);
    }

    public static CandidateAction ActionFor(byte confidence, RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => confidence switch
        {
            >= 95 => CandidateAction.Delete,
            >= 80 => CandidateAction.Review,
            _ => CandidateAction.Keep,
        },
        RiskLevel.Medium => confidence >= 80 ? CandidateAction.Review : CandidateAction.Keep,
        RiskLevel.Advanced => CandidateAction.Keep,
        _ => CandidateAction.Keep,
    };

    public static string Recommendation(byte confidence) => confidence switch
    {
        >= 95 => "Delete",
        >= 80 => "Probably safe",
        >= 50 => "Needs review",
        _ => "Do not touch",
    };

    /// <summary>Returns whole days elapsed since <paramref name="timestamp"/> (0 for future timestamps).</summary>
    public static uint ElapsedDays(DateTimeOffset timestamp)
    {
        var delta = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        return delta > TimeSpan.Zero ? (uint)(delta.TotalDays) : 0u;
    }

    public static uint ElapsedDays(DateTimeOffset? timestamp) =>
        timestamp is { } t ? ElapsedDays(t) : uint.MaxValue;
}
