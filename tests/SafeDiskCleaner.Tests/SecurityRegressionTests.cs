using FluentAssertions;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Rules;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Update;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Tests;

/// <summary>Regression tests for the security-audit round.</summary>
public sealed class SecurityRegressionTests
{
    private static readonly SafetyValidator Validator = new(new SignatureInspector());

    private static string TempDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"safedisk-test-sec-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void JailRejectsPathsOutsideScanRoots()
    {
        var root = TempDir("jail");
        try
        {
            var inside = Path.Combine(root, "sub", "f.tmp");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(inside, "x");
            var verdict = Validator.Validate(inside, Category.Temp, 0, [root]);
            verdict.Allowed.Should().BeTrue();

            var outside = Path.Combine(Path.GetTempPath(), $"safedisk-test-sec-other-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(outside, "x");
            try
            {
                Validator.Validate(outside, Category.Temp, 0, [root]).Allowed.Should().BeFalse();
            }
            finally
            {
                File.Delete(outside);
            }

            // No roots supplied: jail disabled (back-compat), other gates still apply.
            Validator.Validate(inside, Category.Temp, 0).Allowed.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AdsSuffix_IsDenied()
    {
        var verdict = Validator.Validate(@"C:\Temp\safe.txt:evil", Category.Temp, 0);
        verdict.Allowed.Should().BeFalse();
    }

    [Fact]
    public void CatalogOverride_EscapingBase_IsDropped()
    {
        var json = """
            {"groups": [{"id": "evil", "base": "C:\\Safe", "subdirectories": ["..\\..\\Windows\\System32"], "tier": "Always", "join": "Append"}]}
            """;
        var merged = ScanRootsCatalog.Merge(ScanRootsCatalog.Embedded, json);
        merged.Groups.Should().NotContain(g => g.Id == "evil");
    }

    [Fact]
    public void CatalogOverride_ProtectedBase_IsDropped()
    {
        var json = """
            {"groups": [{"id": "evil2", "base": "C:\\Windows\\System32", "subdirectories": [""], "tier": "Always", "join": "Append"}]}
            """;
        var merged = ScanRootsCatalog.Merge(ScanRootsCatalog.Embedded, json);
        merged.Groups.Should().NotContain(g => g.Id == "evil2");
    }

    [Fact]
    public void AssetSelection_RefusesAmbiguity()
    {
        ReleaseAsset Asset(string name) => new() { Name = name, DownloadUrl = "https://github.com/x", Size = 1 };
        var assets = new List<ReleaseAsset>
        {
            Asset("SafeDiskCleaner-1.7.4-portable-win64.exe"),
            Asset("SafeDiskCleaner-1.7.4-portable-win64.exe.sha256"),
        };
        // Result depends on OS; on Windows exactly one must win.
        var selected = UpdateAssets.SelectInstallAsset(assets);
        if (OperatingSystem.IsWindows())
        {
            selected.Should().NotBeNull();
            selected!.Name.Should().Be("SafeDiskCleaner-1.7.4-portable-win64.exe");
        }

        var spoofed = new List<ReleaseAsset>
        {
            Asset("SafeDiskCleaner-1.7.4-portable-win64.exe"),
            Asset("SafeDiskCleaner-9.9.9-portable-win64.exe"),
        };
        if (OperatingSystem.IsWindows())
        {
            UpdateAssets.SelectInstallAsset(spoofed).Should().BeNull();
        }
    }

    [Theory]
    [InlineData("https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/download/v1.0/x.exe", true)]
    [InlineData("http://github.com/ajjs1ajjs/SafeDisk-Cleaner/x.exe", false)]
    [InlineData("https://evil.com/x.exe", false)]
    [InlineData("https://github.com.evil.com/x.exe", false)]
    [InlineData("https://github.com:8443/x", false)]
    [InlineData("", false)]
    public void DownloadUrls_AreAllowlisted(string url, bool expected)
    {
        UpdateUrlValidator.IsAllowedDownloadUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("SafeDiskCleaner-1.7.4-portable-win64.exe", true)]
    [InlineData("../../evil.exe", false)]
    [InlineData("a:b.exe", false)]
    [InlineData("", false)]
    public void AssetNames_AreValidated(string name, bool expected)
    {
        UpdateUrlValidator.IsSafeAssetName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("v1.7.4", true)]
    [InlineData("1.7.4", true)]
    [InlineData("v1.7.4-beta.1", true)]
    [InlineData("../../x", false)]
    [InlineData("v1.7.4/../../x", false)]
    public void VersionTags_AreValidated(string tag, bool expected)
    {
        UpdateUrlValidator.IsSafeVersionTag(tag).Should().Be(expected);
    }

    [Fact]
    public async Task DryRun_DoesNotMutate()
    {
        FakeQuarantine.PurgeCalls = 0;
        var engine = new CleanupEngine(
            new SafetyValidator(new SignatureInspector()),
            new FakeQuarantine(),
            new FakeAudit());
        var result = await engine.RunAsync(
            [],
            new CleanupOptions { Mode = CleanMode.DryRun },
            null,
            CancellationToken.None);
        result.Processed.Should().Be(0);
        FakeQuarantine.PurgeCalls.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAction_RequiresConfirmation()
    {
        var engine = new CleanupEngine(
            new SafetyValidator(new SignatureInspector()),
            new FakeQuarantine(),
            new FakeAudit());
        var candidate = new Candidate
        {
            Path = Path.Combine(Path.GetTempPath(), $"safedisk-test-sec-review-{Guid.NewGuid():N}.tmp"),
            Size = 10,
            Category = Category.DuplicateFiles,
            Confidence = 98,
            Action = CandidateAction.Review,
            Reason = "test",
            RiskLevel = RiskLevel.Advanced,
        };
        File.WriteAllText(candidate.Path, "0123456789");
        try
        {
            var denied = await engine.RunAsync(
                [candidate],
                new CleanupOptions { Mode = CleanMode.Interactive },
                null,
                CancellationToken.None);
            denied.Entries.Should().ContainSingle()
                .Which.Status.Should().Be(CleanupStatus.Failed);

            var allowed = await engine.RunAsync(
                [candidate],
                new CleanupOptions { Mode = CleanMode.Interactive, ConfirmReview = true, MoveToRecycleBin = false, RecencyDays = 0 },
                null,
                CancellationToken.None);
            allowed.Entries.Should().ContainSingle()
                .Which.Status.Should().Be(CleanupStatus.Quarantined);
        }
        finally
        {
            try { File.Delete(candidate.Path); } catch { }
        }
    }

    [Fact]
    public void TrailingDotSystemDir_IsDenied()
    {
        var verdict = Validator.Validate(@"C:\Windows.", Category.Temp, 0);
        verdict.Allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\PROGRA~1\evil.tmp")]
    [InlineData(@"D:\DOCUME~1\user\evil.tmp")]
    [InlineData(@"C:\WINDOWS~1\System32\evil.dll")]
    public void UnexpandedShortName_AtDriveLevel_IsDenied(string path)
    {
        // Lexical second net: works on every OS, no kernel32 required.
        SafeDiskCleaner.Core.Models.PathProtection.IsProtectedPath(path).Should().BeTrue();
    }

    [Fact]
    public void TildeFile_DeeperLevel_IsNotOverblocked()
    {
        var root = TempDir("tilde");
        try
        {
            var file = Path.Combine(root, "sub", "backup~1.tmp");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(file, "x");
            // Short-looking segment below drive level: normal gates apply.
            Validator.Validate(file, Category.Temp, 0, [root]).Allowed.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TmpdirOverride_HostileValue_FallsBackToSafeTemp()
    {
        var previous = Environment.GetEnvironmentVariable("TMPDIR");
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", "/nonexistent-hostile-dir-xyz");
            var resolved = ScanRootsCatalog.ResolveBase("$TMPDIR");
            resolved.Should().NotBeNullOrEmpty();
            resolved.Should().Be(Path.GetTempPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", previous);
        }
    }

    [Fact]
    public void ValidatorFloors_ZeroRecencyAndLowAuto_AreInvalid()
    {
        var scanValidator = new SafeDiskCleaner.Core.Validation.ScanOptionsValidator();
        scanValidator.Validate(new ScanOptions { RecencyDays = 0 }).IsValid.Should().BeFalse();
        scanValidator.Validate(new ScanOptions { RecencyDays = 3651 }).IsValid.Should().BeFalse();

        var cleanupValidator = new SafeDiskCleaner.Core.Validation.CleanupOptionsValidator();
        cleanupValidator.Validate(new CleanupOptions { AutoThreshold = 79 }).IsValid.Should().BeFalse();
        cleanupValidator.Validate(new CleanupOptions { QuarantineRetentionDays = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LockedFile_IsDeniedAsInconclusive()
    {
        var root = TempDir("locked");
        try
        {
            var file = Path.Combine(root, "busy.tmp");
            File.WriteAllText(file, "x");
            using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var verdict = Validator.Validate(file, Category.Temp, 0, [root]);
            verdict.Allowed.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Treemap_NonFiniteAndZeroValues_AreExcluded()
    {
        var tiles = SafeDiskCleaner.Core.Utils.SquarifiedTreemap.Layout(
        [
            new("ok", 10),
            new("nan", double.NaN),
            new("inf", double.PositiveInfinity),
            new("zero", 0),
            new("neg", -5),
        ], 100, 100);
        tiles.Should().HaveCount(1);
        tiles[0].Id.Should().Be("ok");
    }

    [Fact]
    public void ExclusionWildcard_LongInput_CompletesFast()
    {
        var root = TempDir("excl");
        try
        {
            // Absolute wildcard pattern: must match a very long file name
            // quickly (NonBacktracking + timeout guards the regex engine).
            var patterns = new List<string> { Path.Combine(root, "*.log") };
            var target = Path.Combine(root, new string('a', 5000) + ".log");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var matched = PathExclusions.IsExcluded(target, patterns);
            sw.Stop();
            matched.Should().BeTrue();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            // Relative patterns are CWD-scoped by design (canonicalized
            // against the process directory): they must not match elsewhere.
            PathExclusions.IsExcluded(target, ["*.log"]).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeQuarantine : SafeDiskCleaner.Core.Abstractions.IQuarantineService
    {
        public static int PurgeCalls;
        public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
        {
            PurgeCalls++;
            return Task.FromResult(0);
        }

        public Task<string> QuarantineAsync(string sourcePath, uint retentionDays, CancellationToken ct = default)
        {
            try { File.Delete(sourcePath); } catch { }
            return Task.FromResult("fake-id");
        }

        public Task RestoreAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> EmptyAsync(CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<SafeDiskCleaner.Core.Models.QuarantineEntry>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SafeDiskCleaner.Core.Models.QuarantineEntry>>([]);
    }

    private sealed class FakeAudit : SafeDiskCleaner.Core.Abstractions.IAuditService    {
        public Task AppendAsync(SafeDiskCleaner.Core.Models.AuditEntry entry, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AppendManyAsync(IReadOnlyList<SafeDiskCleaner.Core.Models.AuditEntry> entries, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SafeDiskCleaner.Core.Models.AuditEntry>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SafeDiskCleaner.Core.Models.AuditEntry>>([]);

        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
