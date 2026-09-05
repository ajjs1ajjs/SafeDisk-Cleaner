using FluentAssertions;
using SafeDiskCleaner.Core.Utils;
using SafeDiskCleaner.Infrastructure.Services;

namespace SafeDiskCleaner.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("v1.2.3", "1.0.0", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("2.0.0-beta.1", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    public void IsNewerThan_Works(string candidate, string current, bool expected)
    {
        SemanticVersion.IsNewerThan(candidate, current).Should().Be(expected);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsInvalid(string? version)
    {
        SemanticVersion.TryParse(version).Should().BeNull();
    }

    [Fact]
    public void TryParse_StripsVPrefix()
    {
        SemanticVersion.TryParse("v4.5.6").Should().Be((4, 5, 6));
    }
}

public sealed class HumanSizeTests
{
    [Fact]
    public void Bytes_NoUnit()
    {
        HumanSize.Format(512).Should().Be("512 B");
    }

    [Fact]
    public void Kilobytes()
    {
        HumanSize.Format(2048).Should().Be("2.0 KB");
    }

    [Fact]
    public void Gigabytes()
    {
        HumanSize.Format(2L * 1024 * 1024 * 1024).Should().Be("2.00 GB");
    }

    [Fact]
    public void Negative_ReturnsQuestionMark()
    {
        HumanSize.Format(-1).Should().Be("?");
    }
}
