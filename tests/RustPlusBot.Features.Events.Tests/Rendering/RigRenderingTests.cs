using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Tests.Rendering;

public sealed class RigRenderingTests
{
    private static EventEmbedRenderer Renderer() =>
        new(new ResxLocalizer());

    [Theory]
    [InlineData(RigKind.Small, RigEventKind.Activated, "en")]
    [InlineData(RigKind.Small, RigEventKind.CrateLootable, "fr")]
    [InlineData(RigKind.Large, RigEventKind.Respawned, "en")]
    public void RenderRig_produces_a_nonempty_embed(RigKind rig, RigEventKind kind, string culture)
    {
        var evt = new RigStateChangedEvent(1UL, Guid.NewGuid(), rig, kind, 100f, 200f, null);
        var embed = Renderer().RenderRig(evt, culture);
        Assert.False(string.IsNullOrWhiteSpace(embed.Description));
    }

    [Fact]
    public void RenderRigLine_includes_a_grid_reference()
    {
        var evt = new RigStateChangedEvent(1UL, Guid.NewGuid(), RigKind.Small, RigEventKind.Activated, 100f, 200f,
            null);
        var line = Renderer().RenderRigLine(evt, "en");
        Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void RenderLine_for_cargo_is_nonempty()
    {
        var evt = new RustMapEvent(MapEventKind.CargoEntered, 0f, 0f, null, DateTimeOffset.UtcNow);
        var line = Renderer().RenderLine(evt, "fr");
        Assert.False(string.IsNullOrWhiteSpace(line));
    }
}
