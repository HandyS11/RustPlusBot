using RustPlusBot.Features.Alarms.Rendering;

namespace RustPlusBot.Features.Alarms.Tests;

public sealed class AlarmLocalizationCatalogTests
{
    private static readonly string[] RequiredKeys =
    [
        "alarm.status.armed",
        "alarm.status.active",
        "alarm.status.unreachable",
        "alarm.button.ping.on",
        "alarm.button.ping.off",
        "alarm.button.relay.on",
        "alarm.button.relay.off",
        "alarm.button.rename",
        "alarm.embed.footer",
        "alarm.embed.nevertriggered",
        "alarm.embed.lasttriggered",
        "alarm.prompt.title",
        "alarm.prompt.body",
        "alarm.prompt.accept",
        "alarm.prompt.dismiss",
        "alarm.rename.modal.title",
        "alarm.rename.input.label",
        "alarm.triggered.teamchat",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Catalog_contains_all_required_keys(string culture)
    {
        var map = AlarmLocalizationCatalog.Default[culture];
        foreach (var key in RequiredKeys)
        {
            Assert.True(map.ContainsKey(key), $"Missing key '{key}' for culture '{culture}'.");
        }
    }

    [Fact]
    public void En_and_fr_have_identical_key_sets()
    {
        var enKeys = new SortedSet<string>(AlarmLocalizationCatalog.Default["en"].Keys);
        var frKeys = new SortedSet<string>(AlarmLocalizationCatalog.Default["fr"].Keys);
        Assert.Equal(enKeys, frKeys);
    }
}
