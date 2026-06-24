using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventRendererTests
{
    private static readonly PlayerEventRenderer Renderer = new(new ResxLocalizer());
    private static readonly MapDimensions Dims = new(3000, 3000, 0);

    [Fact]
    public void Connect_line_names_member_without_location()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.Connect, 1, "Bob", null), Dims, "en");
        Assert.Equal("Bob connected", line);
    }

    [Fact]
    public void Death_with_location_includes_grid()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.Death, 1, "Bob", (0f, 2999f)), Dims, "en");
        Assert.StartsWith("Bob died at A", line, StringComparison.Ordinal); // top-left ≈ A0/A1
    }

    [Fact]
    public void Death_without_location_uses_unknown_string()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.Death, 1, "Bob", null), Dims, "en");
        Assert.Equal("Bob died", line);
    }

    [Fact]
    public void Afk_back_has_no_location()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.ReturnedFromAfk, 1, "Bob", null), Dims, "en");
        Assert.Equal("Bob is back", line);
    }
}
