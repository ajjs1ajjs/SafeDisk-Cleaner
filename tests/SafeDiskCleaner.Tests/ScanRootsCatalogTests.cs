using FluentAssertions;
using SafeDiskCleaner.Core.Rules;

namespace SafeDiskCleaner.Tests;

public sealed class ScanRootsCatalogTests
{
    [Fact]
    public void EmbeddedCatalog_Loads_AndHasGroups()
    {
        var catalog = ScanRootsCatalog.Embedded;

        catalog.Groups.Should().NotBeEmpty();
        catalog.Groups.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Id));
        catalog.Groups.Select(g => g.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Resolve_ReturnsExistingDirectories_IncludingTemp()
    {
        var catalog = ScanRootsCatalog.Embedded;
        var roots = catalog.Resolve(includeMedium: false, includeAdvanced: false);
        roots.Should().NotBeEmpty("at least the temp directory must resolve on any OS");
        roots.Should().Contain(r => IsTempDir(r), "the user temp dir is an always-tier root");
    }

    [Fact]
    public void Resolve_ExcludesMediumAndAdvancedTiers_UnlessRequested()
    {
        var catalog = new ScanRootsCatalog
        {
            Groups =
            [
                new ScanRootGroup { Id = "always", Base = "$TEMP" },
                new ScanRootGroup { Id = "medium", Base = "$TEMP", Tier = RootTier.Medium, Join = SubPathJoin.Combine, Subdirectories = ["med"] },
                new ScanRootGroup { Id = "advanced", Base = "$TEMP", Tier = RootTier.Advanced, Join = SubPathJoin.Combine, Subdirectories = ["adv"] },
            ],
        };

        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "med"));
        try
        {
            var plain = catalog.Resolve(includeMedium: false, includeAdvanced: false);
            var withMedium = catalog.Resolve(includeMedium: true, includeAdvanced: false);

            plain.Should().NotContain(r => r.EndsWith("med"));
            withMedium.Should().Contain(r => r.EndsWith("med"));

            // advanced tier requires both flags
            withMedium.Should().NotContain(r => r.EndsWith("adv"));
        }
        finally
        {
            Directory.Delete(Path.Combine(Path.GetTempPath(), "med"), recursive: true);
        }
    }

    [Fact]
    public void OsFilter_SkipsForeignPlatforms()
    {
        var foreign = OperatingSystem.IsWindows() ? "linux" : "windows";
        var catalog = new ScanRootsCatalog
        {
            Groups =
            [
                new ScanRootGroup { Id = "foreign-only", Os = [foreign], Base = "/definitely/not/existing" },
                new ScanRootGroup { Id = "current-os", Base = "$TEMP" },
            ],
        };

        var resolved = catalog.Resolve(true, true);
        resolved.Should().Contain(r => r.TrimEnd('/', '\\').EndsWith("mp", StringComparison.OrdinalIgnoreCase));
        resolved.Should().NotContain("/definitely/not/existing");
    }

    [Fact]
    public void Merge_ReplacesById_AndAppendsNewGroups()
    {
        var baseCatalog = new ScanRootsCatalog
        {
            Groups = [
                new ScanRootGroup { Id = "a", Base = "/base-a", Subdirectories = ["x"] },
                new ScanRootGroup { Id = "b", Base = "/base-b" },
            ],
        };

        var merged = ScanRootsCatalog.Merge(baseCatalog, """
        {
          "groups": [
            { "id": "b", "base": "/base-b-override" },
            { "id": "c", "base": "/base-c" }
          ]
        }
        """);

        merged.Groups.Should().HaveCount(3);
        merged.Groups.First(g => g.Id == "b").Base.Should().Be("/base-b-override");
        merged.Groups.Should().Contain(g => g.Id == "c");
        merged.Groups.First(g => g.Id == "a").Base.Should().Be("/base-a", "groups without overrides stay intact");
    }

    [Fact]
    public void LoadOrDefault_IgnoresMalformedOverrideFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdc-rules-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not json ");

            var catalog = ScanRootsCatalog.LoadOrDefault(path);

            catalog.Groups.Should().BeEquivalentTo(ScanRootsCatalog.Embedded.Groups);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveBase_KnownTokens_ResolveOrSkipCleanly()
    {
        ScanRootsCatalog.ResolveBase("$TEMP").Should().NotBeNullOrEmpty();
        ScanRootsCatalog.ResolveBase("$PROFILE").Should().NotBeNullOrEmpty();
        ScanRootsCatalog.ResolveBase("$UNKNOWN_TOKEN").Should().BeNull();
        ScanRootsCatalog.ResolveBase("").Should().BeNull();
    }
    private static bool IsTempDir(string path)
    {
        var clean = path.TrimEnd('/', '\\');
        return clean.EndsWith("tmp", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith("temp", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith("/t", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith("\\T", StringComparison.OrdinalIgnoreCase);
    }
}
