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

    [Theory]
    [InlineData("/System/Library/foo")]
    [InlineData("/usr/bin/foo")]
    [InlineData("/bin/bash")]
    [InlineData("/sbin/fsck")]
    [InlineData("/etc/hosts")]
    [InlineData("/boot/efi")]
    [InlineData("/Library/LaunchAgents/foo")]
    [InlineData("/bin")]
    [InlineData("/etc")]
    public void MacOsProtectedPaths_AreDetected(string path)
    {
        // OS-independent: exercise the macOS branch directly so the test
        // runs on Windows/Linux CI too (regression: '/' vs '\\' needles).
        PathProtection.IsProtectedPath(path, isMacOS: true).Should().BeTrue();
    }

    [Theory]
    [InlineData("/System/Library/foo")]
    [InlineData("/usr/bin/foo")]
    public void MacOsBranch_IsGatedByOS(string path)
    {
        // On non-macOS the same paths are not mac-protected (Windows needles don't match them).
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        PathProtection.IsProtectedPath(path, isMacOS: false).Should().BeFalse();
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

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"D:\Program Files")]
    [InlineData(@"C:\Users\Alice")]
    [InlineData(@"C:\Users\Alice\NTUSER.DAT")]
    [InlineData(@"C:\Program Files \evil.dat")]
    public void BareSystemDirs_AreDetected(string path)
    {
        PathProtection.IsProtectedPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Temp\foo.txt")]
    [InlineData(@"D:\Data\file.dat")]
    [InlineData(@"C:\Users\Alice\Documents\notes.txt")]
    public void NonSystemPaths_AreNotFlagged(string path)
    {
        PathProtection.IsProtectedPath(path).Should().BeFalse();
    }

    [Fact]
    public void NestedWindowsOld_DoesNotDisarmSystemNeedles()
    {
        // windows.old nested deep inside System32 must NOT weaken protection.
        PathProtection.IsProtectedPath(@"C:\Windows\System32\drivers\windows.old\payload.dll").Should().BeTrue();
    }

    [Fact]
    public void AdsSuffix_IsProtected()
    {
        PathProtection.IsProtectedPath(@"C:\Temp\safe.txt:evil").Should().BeTrue();
    }
}
