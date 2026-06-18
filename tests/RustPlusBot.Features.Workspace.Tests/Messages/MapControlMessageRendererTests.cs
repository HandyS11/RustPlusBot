using Discord;
using NSubstitute;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

public sealed class MapControlMessageRendererTests
{
    private static readonly Localizer Loc = new(LocalizationCatalog.Default);

    [Fact]
    public async Task Renders_six_toggle_buttons_reflecting_settings()
    {
        var serverId = Guid.NewGuid();
        var settings = Substitute.For<IMapSettingsStore>();
        // Monuments off, the rest on.
        settings.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new MapLayerSettings(true, true, false, true, true, true));
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Components);
        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
        Assert.Equal(6, buttons.Count);

        var monuments = buttons.Single(b =>
            b.CustomId == $"workspace:map:toggle:{MapLayer.Monuments}:{serverId}");
        Assert.Equal(ButtonStyle.Secondary, monuments.Style);

        foreach (var other in buttons.Where(b => b.CustomId != monuments.CustomId))
        {
            Assert.Equal(ButtonStyle.Success, other.Style);
        }
    }

    [Fact]
    public async Task Renders_a_header_text()
    {
        var serverId = Guid.NewGuid();
        var settings = Substitute.For<IMapSettingsStore>();
        settings.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(MapLayerSettings.AllOn);
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Equal(Loc.Get("map.control.header", "en"), payload.Text);
    }

    [Fact]
    public async Task NullServerId_RendersEmptyPayload()
    {
        var settings = Substitute.For<IMapSettingsStore>();
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, null, "en"), default);

        Assert.Null(payload.Text);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task FrenchCulture_UsesFrenchGridLabel()
    {
        var serverId = Guid.NewGuid();
        var settings = Substitute.For<IMapSettingsStore>();
        settings.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(MapLayerSettings.AllOn);
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "fr"), default);

        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
        var gridLabel = Loc.Get("map.layer.grid", "fr");
        Assert.Contains(buttons, b => b.Label == gridLabel);
    }
}
