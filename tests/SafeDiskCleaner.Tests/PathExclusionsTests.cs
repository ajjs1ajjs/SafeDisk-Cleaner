using FluentAssertions;
using SafeDiskCleaner.Core.Safety;

namespace SafeDiskCleaner.Tests;

public sealed class PathExclusionsTests
{
    [Fact]
    public void EmptyPatterns_NeverExclude()
    {
        PathExclusions.IsExcluded(@"C:\anything\file.tmp", Array.Empty<string>()).Should().BeFalse();
        PathExclusions.IsExcluded(@"C:\anything\file.tmp", ["", "   "]).Should().BeFalse();
    }

    [Fact]
    public void PlainDirectoryPattern_ExcludesSubtree()
    {
        var patterns = new[] { @"C:\Work\Secrets" };

        PathExclusions.IsExcluded(@"C:\Work\Secrets", patterns).Should().BeTrue();
        PathExclusions.IsExcluded(@"C:\Work\Secrets\cache\f_0001.tmp", patterns).Should().BeTrue();
        PathExclusions.IsExcluded(@"C:\work\secrets/CACHE/x.log", patterns).Should().BeTrue("matching is case-insensitive and separator-agnostic");
    }

    [Fact]
    public void PrefixMatch_RespectsDirectoryBoundary()
    {
        var patterns = new[] { @"C:\Temp" };

        PathExclusions.IsExcluded(@"C:\Temp\file.tmp", patterns).Should().BeTrue();
        PathExclusions.IsExcluded(@"C:\Temporary\file.tmp", patterns)
            .Should().BeFalse("'C:\\Temp' must not swallow 'C:\\Temporary'");
    }

    [Fact]
    public void Wildcards_AreSupported()
    {
        var patterns = new[] { @"C:\Logs\*.tmp" };

        PathExclusions.IsExcluded(@"C:\Logs\a.tmp", patterns).Should().BeTrue();
        PathExclusions.IsExcluded(@"C:\Logs\sub\b.tmp", patterns).Should().BeTrue("'*' crosses separators");
        PathExclusions.IsExcluded(@"C:\Logs\a.log", patterns).Should().BeFalse();

        var single = new[] { @"C:\Logs\app??.log" };
        PathExclusions.IsExcluded(@"C:\Logs\app01.log", single).Should().BeTrue();
        PathExclusions.IsExcluded(@"C:\Logs\app1.log", single).Should().BeFalse();
    }

    [Fact]
    public void TrailingSeparator_InPattern_IsTolerated()
    {
        var patterns = new[] { @"C:\Work\Secrets\" };

        PathExclusions.IsExcluded(@"C:\Work\Secrets\data.bin", patterns).Should().BeTrue();
    }
}
