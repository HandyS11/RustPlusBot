using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

public sealed class MapControlMessageRendererTests
{
    private static readonly ResxLocalizer Loc = new();

    private static List<ButtonComponent> Buttons(MessagePayload payload) =>
    [
        .. payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>(),
    ];

    [Fact]
    public async Task Renders_seven_toggle_buttons_reflecting_settings()
    {
        var serverId = Guid.NewGuid();
        var settings = Substitute.For<IMapSettingsStore>();
        // Monuments off, the rest on.
        settings.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new MapLayerSettings(true, true, false, true, true, true));
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Components);
        var toggles = Buttons(payload)
            .Where(b => b.CustomId!.StartsWith(WorkspaceComponentIds.MapTogglePrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(7, toggles.Count);

        var monuments = toggles.Single(b =>
            b.CustomId == $"workspace:map:toggle:{MapLayer.Monuments}:{serverId}");
        Assert.Equal(ButtonStyle.Secondary, monuments.Style);

        foreach (var other in toggles.Where(b => b.CustomId != monuments.CustomId))
        {
            Assert.Equal(ButtonStyle.Success, other.Style);
        }
    }

    [Fact]
    public async Task Renders_grid_style_buttons_marking_the_active_style()
    {
        var serverId = Guid.NewGuid();
        var settings = Substitute.For<IMapSettingsStore>();
        settings.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(MapLayerSettings.AllOn with
            {
                GridStyle = MapGridStyle.RustPlus
            });
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var inGame = Buttons(payload).Single(b =>
            b.CustomId == $"workspace:map:gridstyle:{MapGridStyle.InGame}:{serverId}");
        var rustPlus = Buttons(payload).Single(b =>
            b.CustomId == $"workspace:map:gridstyle:{MapGridStyle.RustPlus}:{serverId}");
        Assert.Equal(ButtonStyle.Primary, rustPlus.Style);
        Assert.Equal(ButtonStyle.Secondary, inGame.Style);
    }

    [Fact]
    public async Task Renders_a_header_text_with_the_grid_style_help()
    {
        var serverId = Guid.NewGuid();
        var settings = Substitute.For<IMapSettingsStore>();
        settings.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(MapLayerSettings.AllOn);
        var renderer = new MapControlMessageRenderer(settings, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.StartsWith(Loc.Get("map.control.header", "en"), payload.Text, StringComparison.Ordinal);
        Assert.Contains(Loc.Get("map.gridstyle.help", "en"), payload.Text, StringComparison.Ordinal);
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
