using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Core.Cleanup;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Safety;
using SafeDiskCleaner.Core.Windows;
using SafeDiskCleaner.Infrastructure.Data;
using SafeDiskCleaner.Infrastructure.Services;

namespace SafeDiskCleaner.Tests;

/// <summary>
/// Integration tests that exercise the real EF Core + SQLite stack and the
/// actual services (quarantine, audit) end-to-end — not mocks.
/// </summary>
public sealed class InfrastructureIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"safedisk-test-it-{Guid.NewGuid():N}");
    private SqliteConnection? _connection;
    private ServiceProvider? _provider;

    private sealed class TestPaths : IAppPaths
    {
        private readonly string _root;

        public TestPaths(string root)
        {
            _root = root;
            DataRoot = root;
            AuditDir = Path.Combine(root, "audit");
            QuarantineDir = Path.Combine(root, "quarantine");
            ReportsDir = Path.Combine(root, "reports");
        }

        public string DataRoot { get; }
        public string AuditDir { get; }
        public string QuarantineDir { get; }
        public string ReportsDir { get; }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(AuditDir);
            Directory.CreateDirectory(QuarantineDir);
            Directory.CreateDirectory(ReportsDir);
        }
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(new TestPaths(_root));
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IQuarantineService, QuarantineService>();

        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _provider?.Dispose();
        await _connection!.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private IAuditService Audit => _provider!.GetRequiredService<IAuditService>();
    private IQuarantineService Quarantine => _provider!.GetRequiredService<IQuarantineService>();

    [Fact]
    public async Task Audit_AppendMany_ThenGetAll_RoundTrips()
    {
        await Audit.AppendManyAsync(
        [
            new AuditEntry { Timestamp = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), Action = "deleted", Path = @"C:\a.tmp", Size = 10, Success = true },
            new AuditEntry { Timestamp = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), Action = "failed", Path = @"C:\b.tmp", Size = 0, Success = false },
        ]);

        var all = await Audit.GetAllAsync();

        all.Should().HaveCount(2);
        all[0].Path.Should().Be(@"C:\a.tmp");
        all[0].Action.Should().Be("deleted");
        all[0].Success.Should().BeTrue();
        all[1].Success.Should().BeFalse();
    }

    [Fact]
    public async Task Quarantine_RoundTrips_Restore_And_Remove()
    {
        var paths = _provider!.GetRequiredService<IAppPaths>();
        paths.EnsureCreated();

        var file = Path.Combine(_root, "victim.tmp");
        File.WriteAllBytes(file, new byte[2048]);

        var id = await Quarantine.QuarantineAsync(file, retentionDays: 14);

        File.Exists(file).Should().BeFalse("source must be moved away");
        var stored = Path.Combine(paths.QuarantineDir, id, "victim.tmp");
        File.Exists(stored).Should().BeTrue("file must be in quarantine");

        var list = await Quarantine.ListAsync();
        list.Should().ContainSingle(e => e.Id == id && e.Size == 2048 && e.OriginalPath == file);

        await Quarantine.RestoreAsync(id);
        File.Exists(file).Should().BeTrue("restore must put the file back");
        File.Exists(stored).Should().BeFalse();

        var id2 = await Quarantine.QuarantineAsync(file, retentionDays: 14);
        await Quarantine.RemoveAsync(id2);
        File.Exists(file).Should().BeFalse();
        (await Quarantine.ListAsync()).Should().NotContain(e => e.Id == id2);
    }

    [Fact]
    public async Task Quarantine_PurgeExpired_RemovesOldEntries_KeepsFresh()
    {
        var paths = _provider!.GetRequiredService<IAppPaths>();
        paths.EnsureCreated();

        var old = Path.Combine(_root, "old.tmp");
        File.WriteAllBytes(old, new byte[16]);
        var fresh = Path.Combine(_root, "fresh.tmp");
        File.WriteAllBytes(fresh, new byte[16]);

        var oldId = await Quarantine.QuarantineAsync(old, retentionDays: 14);
        var freshId = await Quarantine.QuarantineAsync(fresh, retentionDays: 14);

        // Backdate the old entry so it looks expired.
        await using (var db = _provider!.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            var entity = await db.Quarantines.FirstAsync(e => e.Id == oldId);
            entity.QuarantinedAt = DateTime.UtcNow.AddDays(-30);
            entity.ExpiresAt = DateTime.UtcNow.AddDays(-16);
            await db.SaveChangesAsync();
        }

        var purged = await Quarantine.PurgeExpiredAsync(retentionDays: 14);

        purged.Should().Be(1);
        (await Quarantine.ListAsync()).Should().NotContain(e => e.Id == oldId);
        (await Quarantine.ListAsync()).Should().Contain(e => e.Id == freshId);
        Directory.Exists(Path.Combine(paths.QuarantineDir, oldId)).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupEngine_EndToEnd_QuarantinesAndAudits()
    {
        var paths = _provider!.GetRequiredService<IAppPaths>();
        paths.EnsureCreated();

        var file = Path.Combine(_root, "junk.tmp");
        File.WriteAllBytes(file, new byte[1024]);
        File.SetLastAccessTimeUtc(file, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));

        var candidate = new Candidate
        {
            Path = file,
            Size = 1024,
            Category = Category.Temp,
            Confidence = 99,
            Action = CandidateAction.Delete,
            Reason = "test",
            RiskLevel = RiskLevel.Safe,
        };

        var engine = new CleanupEngine(
            new SafetyValidator(new SignatureInspector()),
            Quarantine,
            Audit);

        var result = await engine.RunAsync(
            [candidate],
            new CleanupOptions { Mode = CleanMode.Interactive, MoveToRecycleBin = false, QuarantineRetentionDays = 14 },
            null);

        result.Entries.Should().ContainSingle(e => e.Status == CleanupStatus.Quarantined);
        File.Exists(file).Should().BeFalse();

        var audit = await Audit.GetAllAsync();
        audit.Should().ContainSingle(e => e.Path == file && e.Action == "quarantined" && e.Success);
    }
}
