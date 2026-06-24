using RustPlusBot.Localization;

namespace RustPlusBot.Localization.Tests;

public sealed class DictionaryLocalizerTests
{
    private static DictionaryLocalizer Build() => new(
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["greet"] = "Hello {0}", ["bye"] = "Bye",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["greet"] = "Bonjour {0}"
            },
        });

    [Fact]
    public void Get_ReturnsCultureValue() => Assert.Equal("Bonjour {0}", Build().Get("greet", "fr"));

    [Fact]
    public void Get_NormalizesRegion() => Assert.Equal("Bonjour {0}", Build().Get("greet", "fr-FR"));

    [Fact]
    public void Get_FallsBackToEnglish() => Assert.Equal("Bye", Build().Get("bye", "fr"));

    [Fact]
    public void Get_ReturnsKeyWhenMissing() => Assert.Equal("nope", Build().Get("nope", "en"));

    [Fact]
    public void Get_FormatsArgs() => Assert.Equal("Hello world", Build().Get("greet", "en", "world"));

    [Fact]
    public void Get_BlankCulture_UsesEnglish() => Assert.Equal("Bye", Build().Get("bye", ""));
}
