using FluentAssertions;
using SafeDiskCleaner.Core.Localization;

namespace SafeDiskCleaner.Tests;

public sealed class LocalizationTests
{
    private static readonly string[] Languages = ["uk", "en", "pl"];

    [Fact]
    public void AllCatalogs_HaveIdenticalKeySets()
    {
        var catalogs = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["uk"] = Uk.Map,
            ["en"] = En.Map,
            ["pl"] = Pl.Map,
        };

        var reference = catalogs["uk"].Keys.ToHashSet();
        reference.Should().NotBeEmpty();

        foreach (var lang in new[] { "en", "pl" })
        {
            catalogs[lang].Keys.Should().BeEquivalentTo(
                reference,
                because: $"{lang} catalog must define exactly the same keys as uk");
        }
    }

    [Fact]
    public void Values_DoNotContainEmptyStrings()
    {
        foreach (var value in Uk.Map.Values.Concat(En.Map.Values).Concat(Pl.Map.Values))
        {
            value.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Indexer_ReturnsKey_WhenMissing()
    {
        var service = CreateService("en");
        service["Definitely.Not.A.Real.Key"].Should().Be("Definitely.Not.A.Real.Key");
    }

    [Fact]
    public void SetLanguage_SwitchesTranslations_AndFallsBackForUnknown()
    {
        var service = CreateService("uk");
        service.Language.Should().Be("uk");

        service.SetLanguage("en");
        service["Common.Cancel"].Should().Be("Cancel");
        service.Language.Should().Be("en");

        service.SetLanguage("de");
        service.Language.Should().Be(LocalizationService.DefaultLanguage);
        service["Common.Cancel"].Should().NotBe("Cancel", "unknown language falls back to the default catalog");
    }

    [Fact]
    public void Format_SubstitutesPositionalArgs()
    {
        var service = CreateService("en");
        service.Format("C.DaysShort", 7).Should().Be("7 d.");
    }

    [Fact]
    public async Task SetLanguage_RaisesLanguageChanged_AsyncSubscriberSeesNewValues()
    {
        var service = CreateService("uk");
        var raised = new TaskCompletionSource<bool>();

        service.LanguageChanged += (_, _) => raised.TrySetResult(true);

        // yield first so the subscriber observes the post-change state
        await Task.Yield();
        service.SetLanguage("en");

        (await raised.Task).Should().BeTrue();
        service["Nav.Dashboard"].Should().Be("Dashboard");
    }

    private static LocalizationService CreateService(string initial)
    {
        var service = new LocalizationService();
        service.SetLanguage(initial);
        return service;
    }
}
