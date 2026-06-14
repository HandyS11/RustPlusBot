using RustPlusBot.Features.Workspace.Localization;

namespace RustPlusBot.Features.Workspace.Tests.Localization;

public sealed class LocalizerTests
{
    private static Localizer NewLocalizer() => new(LocalizationCatalog.Default);

    [Fact]
    public void Get_ReturnsCultureSpecificValue()
    {
        var localizer = NewLocalizer();
        Assert.Equal("information", localizer.Get("channel.information.name", "en"));
        Assert.Equal("informations", localizer.Get("channel.information.name", "fr"));
    }

    [Fact]
    public void Get_FallsBackToEnglish_WhenCultureMissing()
    {
        var localizer = NewLocalizer();
        Assert.Equal("information", localizer.Get("channel.information.name", "de"));
    }

    [Fact]
    public void Get_ReturnsKey_WhenMissingEverywhere()
    {
        var localizer = NewLocalizer();
        Assert.Equal("nope.key", localizer.Get("nope.key", "en"));
    }

    [Fact]
    public void Get_NormalizesRegionVariants()
    {
        var localizer = NewLocalizer();
        Assert.Equal("information", localizer.Get("channel.information.name", "en-US"));
    }

    [Fact]
    public void Get_WithArgs_Formats()
    {
        var localizer = NewLocalizer();
        Assert.Contains("3", localizer.Get("information.servers", "en", 3), StringComparison.Ordinal);
    }
}
