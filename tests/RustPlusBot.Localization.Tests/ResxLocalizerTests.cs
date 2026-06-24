using RustPlusBot.Localization;

namespace RustPlusBot.Localization.Tests;

public sealed class ResxLocalizerTests
{
    private static readonly ResxLocalizer Sut = new();

    [Fact]
    public void Get_returns_english_value()
    {
        Assert.Equal("⚡ ON", Sut.Get("switch.status.on", "en"));
    }

    [Fact]
    public void Get_returns_french_value()
    {
        Assert.Equal("⚡ ALLUMÉ", Sut.Get("switch.status.on", "fr"));
    }

    [Fact]
    public void Get_falls_back_to_english_for_unknown_culture()
    {
        Assert.Equal("⚡ ON", Sut.Get("switch.status.on", "de"));
    }

    [Fact]
    public void Get_normalizes_region_specific_culture()
    {
        Assert.Equal("⚡ ALLUMÉ", Sut.Get("switch.status.on", "fr-FR"));
    }

    [Fact]
    public void Get_falls_back_to_english_for_blank_culture()
    {
        Assert.Equal("⚡ ON", Sut.Get("switch.status.on", ""));
    }

    [Fact]
    public void Get_returns_key_when_missing()
    {
        Assert.Equal("nonexistent.key", Sut.Get("nonexistent.key", "en"));
    }

    [Fact]
    public void Get_with_args_formats_english()
    {
        Assert.Equal("Endpoint: 1.2.3.4:28015", Sut.Get("server.info.endpoint", "en", "1.2.3.4", 28015));
    }

    [Fact]
    public void Get_with_args_formats_french()
    {
        Assert.Equal("Adresse : 1.2.3.4:28015", Sut.Get("server.info.endpoint", "fr", "1.2.3.4", 28015));
    }

    [Fact]
    public void Get_with_args_on_missing_key_formats_the_key()
    {
        // Missing key returns the key itself; with no placeholders, args are ignored.
        Assert.Equal("nonexistent.key", Sut.Get("nonexistent.key", "en", "x"));
    }
}
