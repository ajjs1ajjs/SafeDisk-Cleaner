using FluentAssertions;
using SafeDiskCleaner.Core.Models;
using SafeDiskCleaner.Core.Scanning;

namespace SafeDiskCleaner.Tests;

/// <summary>
/// Edge cases for the scanner: missing roots, duplicate-size boundary,
/// and user-defined exclusions in both scan modes.
/// </summary>
public sealed class ScannerEdgeCaseTests
{
    [Fact]
    public async Task ScanDuplicatesAsync_NonexistentRoot_ReturnsEmptyResult()
    {
        var roots = new[]
        {
            Path.Combine(Path.GetTempPath(), $"sdc-missing-{Guid.NewGuid():N}"),
            Path.Combine(Path.GetTempPath(), $"sdc-missing-{Guid.NewGuid():N}"),
        };

        var result = await new Scanner().ScanDuplicatesAsync(roots, CancellationToken.None);

        result.Candidates.Should().BeEmpty();
        result.Summary.TotalCandidates.Should().Be(0);
    }

    [Fact]
    public async Task ScanDuplicatesAsync_IgnoresFilesBelowMinSize()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdc-dupsize-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            // DuplicateMinSize is 4096 — identical 4095-byte files must not be flagged
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[4095]);
            File.WriteAllBytes(Path.Combine(root, "b.bin"), new byte[4095]);

            var result = await new Scanner().ScanDuplicatesAsync([root], CancellationToken.None);

            result.Candidates.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanDuplicatesAsync_FlagsFilesExactlyAtMinSize()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdc-dupedge-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[4096]);
            File.WriteAllBytes(Path.Combine(root, "b.bin"), new byte[4096]);

            var result = await new Scanner().ScanDuplicatesAsync([root], CancellationToken.None);

            result.Candidates.Should().HaveCount(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string MakeScanTree(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdc-excl-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "cache"));
        Directory.CreateDirectory(Path.Combine(dir, "keep"));
        File.WriteAllBytes(Path.Combine(dir, "cache", "f_0001"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(dir, "cache", "f_0002"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(dir, "keep", "g_0001.log"), new byte[512]);
        return dir;
    }

    [Fact]
    public async Task ScanAsync_DoesNotFollowJunctionOutsideScanRoot()
    {
        // A junction/symlink created inside the root is a potential escape: it can
        // point to an unrelated directory whose files would be classified as junk
        // and deleted. The scanner must never descend into reparse points. If the
        // environment cannot create links, the test is skipped.
        var baseDir = Path.Combine(Path.GetTempPath(), $"sdc-link-{Guid.NewGuid():N}");
        var root = Path.Combine(baseDir, "scan");
        var victim = Path.Combine(baseDir, "victim");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(victim);
            File.WriteAllBytes(Path.Combine(victim, "secret.log"), new byte[2048]);

            var link = Path.Combine(root, "junk_link");
            var created = TryCreateDirectoryLink(link, victim);
            if (!created)
            {
                return; // no link support/privilege here — nothing to verify
            }

            var options = new ScanOptions
            {
                Roots = [root],
                MinConfidence = 0,
            };
            var result = await new Scanner().ScanAsync(options, null, CancellationToken.None);

            result.Candidates.Should().NotContain(
                c => c.Path.Contains("secret.log", StringComparison.Ordinal),
                "files reachable only through a junction must never be classified/cleaned");
        }
        finally
        {
            TryDeleteTree(baseDir);
        }
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Dir junctions need no admin; fall back to a symlink if unavailable.
                var psi = new System.Diagnostics.ProcessStartInfo(
                    "cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit(10_000);
                if (p.ExitCode == 0 && Directory.Exists(link))
                {
                    return true;
                }
            }

            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort teardown
        }
    }

    [Fact]
    public async Task ScanAsync_DirectoryExclusion_SkipsSubtree()
    {
        var root = MakeScanTree("dir");
        try
        {
            var options = new ScanOptions
            {
                Roots = [root],
                MinConfidence = 0,
                Exclusions = [Path.Combine(root, "cache")],
            };

            var result = await new Scanner().ScanAsync(options, null, CancellationToken.None);
            var local = result.Candidates.Where(c => c.Path.Contains(root)).ToList();

            local.Should().NotBeEmpty("files outside the exclusion still produce candidates");
            local.Should().NotContain(c => c.Path.Contains("cache"), "the excluded subtree must be skipped entirely");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_WildcardExclusion_MatchesByPattern()
    {
        var root = MakeScanTree("wild");
        try
        {
            var options = new ScanOptions
            {
                Roots = [root],
                MinConfidence = 0,
                Exclusions = [$"{Path.Combine(root, "keep")}\\*.log"],
            };

            var result = await new Scanner().ScanAsync(options, null, CancellationToken.None);
            var local = result.Candidates.Where(c => c.Path.Contains($"{root}{Path.DirectorySeparatorChar}keep")).ToList();

            local.Should().BeEmpty("the wildcard exclusion covers every .log file in keep/");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanDuplicatesAsync_Exclusion_SkipsExcludedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdc-dupexcl-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "vault"));
            var data = new byte[8192];
            Array.Fill(data, (byte)0x5A);
            var a = Path.Combine(root, "a.bin");
            var b = Path.Combine(root, "vault", "b.bin");
            File.WriteAllBytes(a, data);
            File.WriteAllBytes(b, data);

            var withoutExclusions = await new Scanner().ScanDuplicatesAsync([root], CancellationToken.None);
            withoutExclusions.Candidates.Should().HaveCount(1);

            var withExclusions = await new Scanner().ScanDuplicatesAsync(
                [root], CancellationToken.None, [Path.Combine(root, "vault")]);
            withExclusions.Candidates.Should().BeEmpty("with 'vault' excluded only one copy remains visible");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
