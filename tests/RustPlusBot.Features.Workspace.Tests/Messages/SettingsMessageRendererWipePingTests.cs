using Discord;
using NSubstitute;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

/// <summary>Wipe-ping toggle tests for <see cref="SettingsMessageRenderer"/>.</summary>
public sealed class SettingsMessageRendererWipePingTests
{
    private static SettingsMessageRenderer Create(bool ping)
    {
        var store = Substitute.For<IWorkspaceStore>();
        store.GetPingEveryoneOnWipeAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(ping);
        return new SettingsMessageRenderer(store, new ResxLocalizer());
    }

    private static ButtonComponent SingleButton(MessagePayload payload) =>
        payload.Components!.Components
            .OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .Single();

    [Fact]
    public async Task Renders_off_button_by_default()
    {
        var renderer = Create(ping: false);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1UL, null, "en"), CancellationToken.None);

        var button = SingleButton(payload);
        Assert.Equal(SettingsMessageRenderer.WipePingButtonId, button.CustomId);
        Assert.Equal(ButtonStyle.Secondary, button.Style);
        Assert.Equal("🔕 Ping @everyone on wipe: Off", button.Label);
    }

    [Fact]
    public async Task Renders_on_button_when_enabled()
    {
        var renderer = Create(ping: true);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1UL, null, "en"), CancellationToken.None);

        var button = SingleButton(payload);
        Assert.Equal(ButtonStyle.Success, button.Style);
        Assert.Equal("🔔 Ping @everyone on wipe: On", button.Label);
    }

    [Fact]
    public async Task Language_select_menu_is_still_present()
    {
        var renderer = Create(ping: false);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1UL, null, "en"), CancellationToken.None);

        var menu = payload.Components!.Components
            .OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<SelectMenuComponent>()
            .Single();
        Assert.Equal(SettingsMessageRenderer.LanguageSelectId, menu.CustomId);
    }
}
