using RustPlusBot.Features.Players.Rendering;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerLocalizationCatalogTests
{
    private static readonly string[] Keys =
    [
        "player.connect", "player.connect.line",
        "player.disconnect", "player.disconnect.line",
        "player.death", "player.death.line",
        "player.death.unknown", "player.death.unknown.line",
        "player.respawn", "player.respawn.line",
        "player.afk", "player.afk.line",
        "player.afk.back", "player.afk.back.line",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Every_key_present_for_culture(string culture)
    {
        var catalog = PlayerLocalizationCatalog.Default;
        Assert.True(catalog.Strings.TryGetValue(culture, out var map));
        foreach (var key in Keys)
        {
            Assert.True(map!.ContainsKey(key), $"missing {key} for {culture}");
        }
    }

    [Fact]
    public void Localizer_formats_with_args_and_falls_back_to_english()
    {
        var localizer = new PlayerLocalizer(PlayerLocalizationCatalog.Default);
        Assert.Contains("Bob", localizer.Get("player.connect", "en", "Bob"), StringComparison.Ordinal);
        // Unknown culture falls back to English string (not the raw key).
        Assert.Contains("Bob", localizer.Get("player.connect", "de", "Bob"), StringComparison.Ordinal);
    }
}
