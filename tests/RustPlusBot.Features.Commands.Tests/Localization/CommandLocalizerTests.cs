using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Tests.Localization;

public sealed class CommandLocalizerTests
{
    private static readonly CommandLocalizer Sut = new(CommandLocalizationCatalog.Default);

    [Fact]
    public void ReturnsFrench_WhenCultureFr() =>
        Assert.Equal("Bot mis en sourdine.", Sut.Get("command.mute.done", "fr"));

    [Fact]
    public void FallsBackToEnglish_WhenCultureUnknown() =>
        Assert.Equal("Bot muted.", Sut.Get("command.mute.done", "xx"));

    [Fact]
    public void FormatsArgs() =>
        Assert.Equal("Pop: 5/100 (2 queued)", Sut.Get("command.pop.ok", "en", 5, 100, 2));

    [Fact]
    public void NormalizesRegion() =>
        Assert.Equal("Bot mis en sourdine.", Sut.Get("command.mute.done", "fr-FR"));
}
