using FluentAssertions;
using SafeDiskCleaner.Core.Confidence;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Tests;

public sealed class ConfidenceEngineTests
{
    private static ConfidenceInput Input(byte baseConfidence, Category category = Category.Temp) => new()
    {
        Base = baseConfidence,
        Category = category,
        Size = 1024,
        LastAccess = null,
        RecencyDays = 7,
        Locked = false,
        SystemAttr = false,
    };

    private static ConfidenceInput With(ConfidenceInput source, DateTimeOffset? lastAccess = null, bool? locked = null, bool? systemAttr = null) => new()
    {
        Base = source.Base,
        Category = source.Category,
        Size = source.Size,
        LastAccess = lastAccess ?? source.LastAccess,
        RecencyDays = source.RecencyDays,
        Locked = locked ?? source.Locked,
        SystemAttr = systemAttr ?? source.SystemAttr,
    };

    [Fact]
    public void BaseConfidence_IsPreservedWithoutFactors()
    {
        ConfidenceEngine.Compute(Input(95)).Should().Be(95);
    }

    [Fact]
    public void RecentAccess_ReducesScore()
    {
        var input = With(Input(99), lastAccess: DateTimeOffset.UtcNow.AddHours(-1));
        ConfidenceEngine.Compute(input).Should().BeLessThan(90);
    }

    [Fact]
    public void OldAccess_IncreasesScore()
    {
        var input = With(Input(90), lastAccess: DateTimeOffset.UtcNow.AddDays(-90));
        var score = ConfidenceEngine.Compute(input);
        score.Should().BeGreaterThan(90);
        score.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void LockedAndSystemAttrs_HeavilyPenalize()
    {
        var input = With(Input(99), locked: true, systemAttr: true);
        ConfidenceEngine.Compute(input).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void MediumRisk_ReducesScore()
    {
        ConfidenceEngine.Compute(Input(90, Category.WindowsUpdateCache)).Should().Be(82);
    }

    [Fact]
    public void Score_IsClampedTo100()
    {
        var input = With(Input(100), lastAccess: DateTimeOffset.UtcNow.AddDays(-400));
        ConfidenceEngine.Compute(input).Should().Be(100);
    }

    [Fact]
    public void Action_SafeHighConfidence_IsDelete()
    {
        ConfidenceEngine.ActionFor(97, RiskLevel.Safe).Should().Be(CandidateAction.Delete);
        ConfidenceEngine.ActionFor(95, RiskLevel.Safe).Should().Be(CandidateAction.Delete);
    }

    [Fact]
    public void Action_SafeMidConfidence_IsReview()
    {
        ConfidenceEngine.ActionFor(85, RiskLevel.Safe).Should().Be(CandidateAction.Review);
    }

    [Fact]
    public void Action_SafeLowConfidence_IsKeep()
    {
        ConfidenceEngine.ActionFor(40, RiskLevel.Safe).Should().Be(CandidateAction.Keep);
    }

    [Fact]
    public void Action_Medium_NeverDeletes()
    {
        ConfidenceEngine.ActionFor(99, RiskLevel.Medium).Should().Be(CandidateAction.Review);
        ConfidenceEngine.ActionFor(50, RiskLevel.Medium).Should().Be(CandidateAction.Keep);
    }

    [Fact]
    public void Action_Advanced_AlwaysKeeps()
    {
        ConfidenceEngine.ActionFor(100, RiskLevel.Advanced).Should().Be(CandidateAction.Keep);
    }

    [Theory]
    [InlineData(100, "Delete")]
    [InlineData(80, "Probably safe")]
    [InlineData(50, "Needs review")]
    [InlineData(10, "Do not touch")]
    public void Recommendation_Ranges(byte confidence, string expected)
    {
        ConfidenceEngine.Recommendation(confidence).Should().Be(expected);
    }

    [Fact]
    public void ElapsedDays_IsZeroForFutureTimestamps()
    {
        ConfidenceEngine.ElapsedDays(DateTimeOffset.UtcNow.AddMinutes(5)).Should().Be(0);
    }

    [Fact]
    public void ElapsedDays_IsManyForEpoch()
    {
        ConfidenceEngine.ElapsedDays(DateTimeOffset.UnixEpoch).Should().BeGreaterThan(20000);
    }
}
