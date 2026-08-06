using FluentAssertions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Tests;

public sealed class SafetyValidatorTests
{
    private static readonly SafetyValidator Validator = new(new SignatureInspector());

    private static string TempDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"safedisk-test-safety-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void EmptyPath_IsDenied()
    {
        Validator.Validate("", Category.Temp, 0).Allowed.Should().BeFalse();
    }

    [Fact]
    public void PathWithoutFilename_IsDenied()
    {
        Validator.Validate(@"C:\", Category.Temp, 0).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ProtectedExtension_IsDenied()
    {
        var verdict = Validator.Validate(@"C:\Temp\foo.dll", Category.Temp, 0);
        verdict.Allowed.Should().BeFalse();
        verdict.Reasons[0].Should().Contain("Protected extension");
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp\foo.txt")]
    [InlineData(@"C:\Windows\System32\foo.txt")]
    [InlineData(@"C:\Program Files\foo\foo.txt")]
    [InlineData(@"C:\ProgramData\foo\foo.txt")]
    public void ProtectedSystemPath_IsDenied(string path)
    {
        Validator.Validate(path, Category.Temp, 0).Allowed.Should().BeFalse();
    }

    [Fact]
    public void SafeDiskInternalPath_IsDenied()
    {
        Validator.Validate(@"C:\ProgramData\SafeDisk\quarantine\abc\file.txt", Category.Temp, 0)
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void RecycleBinSentinel_IsDenied()
    {
        Validator.Validate("__recycle_bin__", Category.RecycleBin, 0).Allowed.Should().BeFalse();
    }

    [Fact]
    public void FreshFile_IsDeniedByRecency()
    {
        var dir = TempDir("fresh");
        var file = Path.Combine(dir, "fresh.txt");
        File.WriteAllText(file, "hello");

        var verdict = Validator.Validate(file, Category.Temp, 1_000_000);
        verdict.Allowed.Should().BeFalse();
        verdict.Reasons[0].Should().Contain("accessed");
    }

    [Fact]
    public void NormalFile_IsAllowed()
    {
        var dir = TempDir("normal");
        var file = Path.Combine(dir, "normal.txt");
        File.WriteAllText(file, "hello");

        var verdict = Validator.Validate(file, Category.Temp, 0);
        verdict.Allowed.Should().BeTrue();
    }

    [Fact]
    public void AdvancedCategory_UnsignedFile_IsAllowed()
    {
        var dir = TempDir("unsigned");
        var file = Path.Combine(dir, "unsigned.txt");
        File.WriteAllText(file, "not signed");

        var verdict = Validator.Validate(file, Category.DuplicateFiles, 0);
        verdict.Allowed.Should().BeTrue();
    }
}
