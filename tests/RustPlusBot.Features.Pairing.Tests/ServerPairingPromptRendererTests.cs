using Discord;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class ServerPairingPromptRendererTests
{
    private static ServerPairingPromptRenderer Create() => new(new ResxLocalizer());

    [Fact]
    public void RenderPrompt_shows_server_identity_and_carries_endpoint_tail()
    {
        var (embed, components) = Create().RenderPrompt("Rustopia", "1.2.3.4", 28015, "en");

        Assert.Equal("New server detected", embed.Title);
        Assert.Contains("Rustopia", embed.Description, StringComparison.Ordinal);
        Assert.Contains("1.2.3.4:28015", embed.Description, StringComparison.Ordinal);

        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<ButtonComponent>().ToList();
        Assert.Contains(buttons, b => b.CustomId == $"{ServerPairingComponentIds.AcceptPrefix}1.2.3.4:28015");
        Assert.Contains(buttons, b => b.CustomId == $"{ServerPairingComponentIds.DismissPrefix}1.2.3.4:28015");
    }

    [Fact]
    public void RenderPrompt_french_uses_french_copy()
    {
        var (embed, _) = Create().RenderPrompt("Rustopia", "1.2.3.4", 28015, "fr");

        Assert.Equal("Nouveau serveur détecté", embed.Title);
    }

    [Fact]
    public void RenderAdded_has_no_buttons()
    {
        var (embed, components) = Create().RenderAdded("Rustopia", "en");

        Assert.Equal("Server added", embed.Title);
        Assert.Contains("Rustopia", embed.Description, StringComparison.Ordinal);
        Assert.Empty(components.Components);
    }

    [Theory]
    [InlineData("1.2.3.4:28015", "1.2.3.4", 28015)]
    [InlineData("play.example.com:28083", "play.example.com", 28083)]
    [InlineData("2001:db8::1:28015", "2001:db8::1", 28015)]
    public void TryParseTail_round_trips_endpoints(string tail, string expectedIp, int expectedPort)
    {
        Assert.True(ServerPairingComponentIds.TryParseTail(tail, out var ip, out var port));
        Assert.Equal(expectedIp, ip);
        Assert.Equal(expectedPort, port);
        Assert.Equal(tail, ServerPairingComponentIds.Tail(ip, port));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-port")]
    [InlineData(":28015")]
    [InlineData("1.2.3.4:")]
    [InlineData("1.2.3.4:notaport")]
    [InlineData("1.2.3.4:0")]
    [InlineData("1.2.3.4:70000")]
    public void TryParseTail_rejects_invalid_tails(string? tail)
    {
        Assert.False(ServerPairingComponentIds.TryParseTail(tail, out _, out _));
    }
}
