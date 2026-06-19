using RustPlusBot.Features.Switches.Rendering;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchLocalizationCatalogTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Catalog_contains_all_required_keys(string culture)
    {
        var map = SwitchLocalizationCatalog.Default.Strings[culture];
        foreach (var key in new[]
                 {
                     "switch.status.on", "switch.status.off", "switch.status.unreachable", "switch.button.on",
                     "switch.button.off", "switch.button.strobe", "switch.button.rename", "switch.prompt.title",
                     "switch.prompt.body", "switch.prompt.accept", "switch.prompt.dismiss", "switch.rename.modal.title",
                     "switch.rename.input.label", "switch.unreachable.ephemeral",
                 })
        {
            Assert.True(map.ContainsKey(key), $"Missing key '{key}' for culture '{culture}'.");
        }
    }

    [Fact]
    public void Localizer_falls_back_to_english()
    {
        var localizer = new SwitchLocalizer(SwitchLocalizationCatalog.Default);
        Assert.Equal(
            SwitchLocalizationCatalog.Default.Strings["en"]["switch.status.on"],
            localizer.Get("switch.status.on", "de"));
    }
}
