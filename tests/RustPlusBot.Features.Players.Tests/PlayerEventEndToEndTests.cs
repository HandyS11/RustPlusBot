using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Rendering;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventEndToEndTests
{
    [Fact]
    public void All_transition_kinds_render_without_throwing()
    {
        var renderer = new PlayerEventRenderer(new PlayerLocalizer(PlayerLocalizationCatalog.Default));
        var dims = new MapDimensions(3000, 3000, 0);
        foreach (PlayerTransitionKind kind in Enum.GetValues<PlayerTransitionKind>())
        {
            var loc = kind is PlayerTransitionKind.Death or PlayerTransitionKind.Respawn or PlayerTransitionKind.BecameAfk
                ? ((float, float)?)(10f, 10f) : null;
            var t = new PlayerTransition(kind, 1, "Bob", loc);
            Assert.NotNull(renderer.Render(t, dims, "en"));
            Assert.NotEmpty(renderer.RenderLine(t, dims, "fr"));
        }
    }
}
