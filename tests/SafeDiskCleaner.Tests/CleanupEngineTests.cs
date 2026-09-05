using FluentAssertions;
using Moq;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Windows;

namespace SafeDiskCleaner.Tests;

public sealed class CleanupEngineTests
{
    private static string OldTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"safedisk-test-clean-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "old.tmp");
        File.WriteAllBytes(file, new byte[1024]);
        File.SetLastAccessTimeUtc(file, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));
        return file;
    }

    private static Candidate SafeCandidate(string path, byte confidence = 99) => new()
    {
        Path = path,
        Size = 1024,
        Category = Category.Temp,
        Confidence = confidence,
        Action = confidence >= 95 ? CandidateAction.Delete : CandidateAction.Review,
        Reason = "test",
        RiskLevel = RiskLevel.Safe,
    };

    private static (CleanupEngine Engine, Mock<IQuarantineService> Quarantine, Mock<IAuditService> Audit) CreateEngine()
    {
        var quarantine = new Mock<IQuarantineService>();
        quarantine.Setup(q => q.QuarantineAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("mock-id");

        var audit = new Mock<IAuditService>();
        var engine = new CleanupEngine(new SafetyValidator(new SignatureInspector()), quarantine.Object, audit.Object);
        return (engine, quarantine, audit);
    }

    [Fact]
    public async Task DryRun_MarksWouldDelete_AndDoesNotTouchFiles()
    {
        var file = OldTempFile();
        try
        {
            var (engine, quarantine, audit) = CreateEngine();
            var options = new CleanupOptions { Mode = CleanMode.DryRun };

            var result = await engine.RunAsync([SafeCandidate(file)], options, null);

            result.Mode.Should().Be(CleanMode.DryRun);
            result.Entries.Should().ContainSingle();
            result.Entries[0].Status.Should().Be(CleanupStatus.WouldDelete);
            result.FreedBytes.Should().Be(1024);
            File.Exists(file).Should().BeTrue("dry-run must not touch the file");
            quarantine.Verify(q => q.QuarantineAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Never);
            audit.Verify(a => a.AppendManyAsync(It.IsAny<IReadOnlyList<AuditEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public async Task Auto_SkipsNonSafeRiskLevel()
    {
        var (engine, quarantine, audit) = CreateEngine();
        var baseCandidate = SafeCandidate(OldTempFile());
        var candidate = new Candidate
        {
            Path = baseCandidate.Path,
            Size = baseCandidate.Size,
            Category = baseCandidate.Category,
            Confidence = baseCandidate.Confidence,
            Action = baseCandidate.Action,
            Reason = baseCandidate.Reason,
            RiskLevel = RiskLevel.Medium,
        };
        var options = new CleanupOptions { Mode = CleanMode.Auto, AutoThreshold = 95 };

        var result = await engine.RunAsync([candidate], options, null);

        result.Entries.Should().ContainSingle();
        result.Entries[0].Status.Should().Be(CleanupStatus.Failed);
        result.Entries[0].Detail.Should().Contain("Auto mode skips");
        quarantine.Verify(q => q.QuarantineAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Auto_SkipsBelowThreshold()
    {
        var (engine, quarantine, audit) = CreateEngine();
        var candidate = SafeCandidate(OldTempFile(), confidence: 90);
        var options = new CleanupOptions { Mode = CleanMode.Auto, AutoThreshold = 95 };

        var result = await engine.RunAsync([candidate], options, null);

        result.Entries.Should().ContainSingle();
        result.Entries[0].Status.Should().Be(CleanupStatus.Failed);
        result.Entries[0].Detail.Should().Contain("below auto threshold");
    }

    [Fact]
    public async Task Interactive_QuarantinesWhenRecycleBinDisabled_AndWritesAudit()
    {
        var file = OldTempFile();
        try
        {
            var (engine, quarantine, audit) = CreateEngine();
            var options = new CleanupOptions
            {
                Mode = CleanMode.Interactive,
                MoveToRecycleBin = false,
                QuarantineRetentionDays = 14,
            };

            var result = await engine.RunAsync([SafeCandidate(file)], options, null);

            result.Entries.Should().ContainSingle();
            result.Entries[0].Status.Should().Be(CleanupStatus.Quarantined);
            quarantine.Verify(q => q.QuarantineAsync(file, 14, It.IsAny<CancellationToken>()), Times.Once);
            audit.Verify(a => a.AppendManyAsync(
                It.Is<IReadOnlyList<AuditEntry>>(entries =>
                    entries.Count == 1
                    && entries[0].Success
                    && entries[0].Path == file
                    && entries[0].Action == "quarantined"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }
}
