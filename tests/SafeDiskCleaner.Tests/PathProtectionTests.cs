using FluentAssertions;
using SafeDiskCleaner.Core.Models;

namespace SafeDiskCleaner.Tests;

public sealed class PathProtectionTests
{
    [Theory]
    [InlineData(@"C:\Windows\Temp\foo.txt")]
    [InlineData(@"C:\windows\system32\drivers\etc\hosts")]
    [InlineData(@"C:\Program Files\SomeApp\file.dat")]
    [InlineData(@"C:\ProgramData\SomeApp\file.dat")]
    [InlineData(@"C:\Windows\boot\file.dat")]
    public void ProtectedPaths_AreDetected(string path)
    {
        PathProtection.IsProtectedPath(path).Should().BeTrue();
    }

    [Fact]
    public void WindowsOld_IsNotFlagged()
    {
        PathProtection.IsProtectedPath(@"C:\Windows.old\foo.txt").Should().BeFalse();
    }

    [Fact]
    public void RegularUserPath_IsNotFlagged()
    {
        PathProtection.IsProtectedPath(@"C:\Users\Someone\AppData\Local\Temp\x.tmp").Should().BeFalse();
    }

    [Fact]
    public void PathWithDotDot_ResolvesThroughCanonicalization()
    {
        // Even if the literal string does not contain a protected needle,
        // canonicalization must catch a traversal into a protected directory.
        PathProtection.IsProtectedPath(@"C:\Users\Me\..\..\Windows\System32\x.dll").Should().BeTrue();
    }
}
