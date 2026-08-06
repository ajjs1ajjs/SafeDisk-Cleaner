using FluentAssertions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Scanning;

namespace SafeDiskCleaner.Tests;

public sealed class ScannerTests
{
    private static string TestRoot(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"safedisk-test-scan-{label}-{Guid.NewGuid():N}");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        Directory.CreateDirectory(Path.Combine(dir, "cache"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        File.WriteAllBytes(Path.Combine(dir, "cache", "f_0001"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(dir, "cache", "f_0002"), new byte[4096]);
        File.WriteAllText(Path.Combine(dir, "logs", "app.log"), "log line\n");
        File.WriteAllBytes(Path.Combine(dir, "tool.exe"), new byte[8192]);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "keep me");
        return dir;
    }

    private static ScanOptions RecencyFree(ScanOptions options) => new()
    {
        Roots = options.Roots,
        IncludeMedium = options.IncludeMedium,
        IncludeAdvanced = options.IncludeAdvanced,
        MinConfidence = options.MinConfidence,
        RecencyDays = 0,
    };

    [Fact]
    public void Scan_FindsCandidatesAndSkipsProtected()
    {
        var root = TestRoot("find");
        try
        {
            var options = RecencyFree(new ScanOptions
            {
                Roots = [root],
                MinConfidence = 0,
            });
            var result = new Scanner().Scan(options, null, CancellationToken.None);

            var local = result.Candidates.Where(c => c.Path.Contains(root)).ToList();

            local.Any(c => c.Path.EndsWith("notes.txt")).Should().BeTrue("notes.txt should be a temp candidate");
            local.Any(c => c.Path.Contains("cache")).Should().BeTrue("cache files should be candidates");
            local.Any(c => c.Path.EndsWith("app.log")).Should().BeTrue("app.log should be a log candidate");
            local.Any(c => c.Path.EndsWith("tool.exe")).Should().BeFalse("protected .exe must never be a candidate");
            result.Summary.ScannedFiles.Should().BeGreaterThanOrEqualTo(4);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_RespectsMinConfidence()
    {
        var root = TestRoot("conf");
        try
        {
            var low = new Scanner().Scan(RecencyFree(new ScanOptions { Roots = [root], MinConfidence = 50 }), null, CancellationToken.None);
            var high = new Scanner().Scan(RecencyFree(new ScanOptions { Roots = [root], MinConfidence = 100 }), null, CancellationToken.None);

            var lowLocal = low.Candidates.Count(c => c.Path.Contains(root));
            var highLocal = high.Candidates.Count(c => c.Path.Contains(root));

            lowLocal.Should().BeGreaterThanOrEqualTo(highLocal, "lower threshold must find at least as many candidates");
            high.Candidates.Where(c => c.Path.Contains(root))
                .All(c => c.Confidence >= 100)
                .Should().BeTrue("no candidate may fall below min_confidence");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_CandidateCarriesReason()
    {
        var root = TestRoot("reason");
        try
        {
            var result = new Scanner().Scan(RecencyFree(new ScanOptions { Roots = [root], MinConfidence = 0 }), null, CancellationToken.None);
            result.Candidates.Where(c => c.Path.Contains(root))
                .All(c => !string.IsNullOrEmpty(c.Reason))
                .Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanDuplicates_DetectsIdenticalFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"safedisk-test-dup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var a = Path.Combine(root, "a.bin");
            var b = Path.Combine(root, "b.bin");
            var c = Path.Combine(root, "c.bin");
            var data = new byte[5000];
            Array.Fill(data, (byte)0xAB);
            File.WriteAllBytes(a, data);
            File.WriteAllBytes(b, data);
            File.WriteAllBytes(c, new byte[] { 1, 2, 3, 4, 5 });

            var result = new Scanner().ScanDuplicates([root], CancellationToken.None);
            result.Candidates.Should().HaveCount(1, "only one duplicate among two identical files");
            result.Candidates[0].Category.Should().Be(Category.DuplicateFiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HashFile_IsDeterministic()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"safedisk-test-hash-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "x.bin");
            File.WriteAllBytes(file, new byte[100_000]);

            var h1 = Scanner.HashFile(file);
            var h2 = Scanner.HashFile(file);

            h1.Should().NotBeNull();
            h1.Should().BeEquivalentTo(h2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Scan_StreamsProgressEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"safedisk-test-progress-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            for (var i = 0; i < 600; i++)
            {
                File.WriteAllBytes(Path.Combine(root, $"f{i:D4}.log"), new byte[128]);
            }

            var events = new List<double>();
            var options = RecencyFree(new ScanOptions { Roots = [root], MinConfidence = 0 });
            new Scanner().Scan(options, p => events.Add(p.Percent), CancellationToken.None);

            events.Should().NotBeEmpty();
            events.Should().Contain(p => p > 0 && p < 100, "expected intermediate percent");
            events.Last().Should().Be(100.0, "last event must be 100%");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
