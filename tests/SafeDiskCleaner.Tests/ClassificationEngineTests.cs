using FluentAssertions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Rules;

namespace SafeDiskCleaner.Tests;

public sealed class ClassificationEngineTests
{
    [Theory]
    [InlineData(@"C:\Temp\foo.dll")]
    [InlineData(@"C:\Temp\foo.sys")]
    [InlineData(@"C:\Temp\setup.exe")]
    [InlineData(@"C:\Temp\patch.msi")]
    public void ProtectedExtensions_AreProtected(string path)
    {
        ClassificationEngine.IsProtectedExtension(path).Should().BeTrue();
    }

    [Fact]
    public void NormalExtension_IsNotProtected()
    {
        ClassificationEngine.IsProtectedExtension(@"C:\Temp\foo.txt").Should().BeFalse();
    }

    [Fact]
    public void ProtectedExtension_IsClassifiedAsProtected()
    {
        ClassificationEngine.Classify(@"C:\Windows\Temp\evil.exe").Kind.Should().Be(MatchKind.Protected);
    }

    [Fact]
    public void MemoryDump_IsCrashDump()
    {
        var result = ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\CrashDumps\memdmp.dmp");
        result.Kind.Should().Be(MatchKind.Candidate);
        result.Category.Should().Be(Category.CrashDump);
    }

    [Fact]
    public void Crashpad_IsCrashDump()
    {
        var result = ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\Google\Chrome\User Data\Crashpad\reports\abc.dmp");
        result.Category.Should().Be(Category.CrashDump);
    }

    [Fact]
    public void ChromeCache_IsBrowserCache()
    {
        var result = ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\Google\Chrome\User Data\Default\Cache\f_00001");
        result.Category.Should().Be(Category.BrowserCache);
    }

    [Fact]
    public void EdgeCodeCache_IsBrowserCache()
    {
        var result = ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\Microsoft\Edge\User Data\Default\Code Cache\js\1.js");
        result.Category.Should().Be(Category.BrowserCache);
    }

    [Fact]
    public void WindowsUpdateDownload_IsUpdateCache()
    {
        var result = ClassificationEngine.Classify(@"C:\Windows\SoftwareDistribution\Download\1\2.cab");
        result.Category.Should().Be(Category.WindowsUpdateCache);
    }

    [Theory]
    [InlineData(@"C:\Users\u\AppData\Local\NuGet\Cache\a.nupkg")]
    [InlineData(@"C:\Users\u\AppData\Local\npm-cache\_cacache\abc")]
    [InlineData(@"C:\Users\u\AppData\Local\pip\cache\http\abc")]
    [InlineData(@"C:\ProgramData\packagecache\file")]
    [InlineData(@"C:\Users\u\AppData\Local\pnpm-store\v3\files\ab")]
    [InlineData(@"C:\Users\u\.cargo\registry\cache\index.crates.io-6f17d22bba15001f\abc")]
    [InlineData(@"C:\Users\u\.gradle\caches\modules-2\files-2.1\com.example")]
    [InlineData(@"C:\Users\u\.yarn\berry\cache\abc")]
    [InlineData(@"C:\Users\u\.bun\install\cache\abc")]
    public void PackageCaches_AreDetected(string path)
    {
        var result = ClassificationEngine.Classify(path);
        result.Kind.Should().Be(MatchKind.Candidate);
        result.Category.Should().Be(Category.PackageCache);
    }

    [Fact]
    public void TempFiles_AreDetected()
    {
        var result = ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\Temp\foo.tmp");
        result.Category.Should().Be(Category.Temp);
    }

    [Fact]
    public void LogFiles_AreDetected()
    {
        var result = ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\app\installer.log");
        result.Category.Should().Be(Category.Logs);
    }

    [Fact]
    public void DriverStore_IsNotClassifiedAsCache()
    {
        // SECURITY: the Windows DriverStore holds installed driver packages;
        // it must never be offered for deletion.
        var result = ClassificationEngine.Classify(@"C:\Windows\DriverStore\FileRepository\foo\file.txt");
        result.Kind.Should().Be(MatchKind.None);
    }

    [Fact]
    public void UnrelatedFile_IsNone()
    {
        ClassificationEngine.Classify(@"C:\Users\u\Documents\report.pdf").Kind.Should().Be(MatchKind.None);
    }

    [Fact]
    public void CaseInsensitiveClassification()
    {
        var result = ClassificationEngine.Classify(@"C:\WINDOWS\SOFTWAREDISTRIBUTION\DOWNLOAD\x\y");
        result.Category.Should().Be(Category.WindowsUpdateCache);
    }

    [Theory]
    [InlineData(@"C:\Users\u\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db")]
    [InlineData(@"C:\Users\u\AppData\Local\Microsoft\Windows\Explorer\iconcache_64.db")]
    public void ThumbnailCache_IsDetected(string path)
    {
        var result = ClassificationEngine.Classify(path);
        result.Category.Should().Be(Category.ThumbnailCache);
    }

    [Fact]
    public void UnrelatedExplorerDb_IsNotThumbnailCache()
    {
        ClassificationEngine.Classify(@"C:\Users\u\AppData\Local\Microsoft\Windows\Explorer\otherfile.dat")
            .Kind.Should().Be(MatchKind.None);
    }

    [Fact]
    public void OldWindowsInstall_IsDetected()
    {
        var result = ClassificationEngine.Classify(@"C:\Windows.old\Windows\System32\config\software");
        result.Category.Should().Be(Category.OldWindowsInstall);
    }

    [Fact]
    public void BaseConfidence_IsWithinRange()
    {
        foreach (var category in Enum.GetValues<Category>())
        {
            var result = ClassificationEngine.Classify(
                $@"C:\Somewhere\Candidate{category}.bin");
            if (result.Kind == MatchKind.Candidate)
            {
                result.BaseConfidence.Should().BeInRange(0, 100);
            }
        }
    }
}
