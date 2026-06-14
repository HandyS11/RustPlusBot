using Discord;
using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

public sealed class RendererTests
{
    private static readonly Localizer Loc = new(LocalizationCatalog.Default);
    private static readonly MessageRenderContext Global = new(1, null, "en");

    [Fact]
    public async Task Information_ShowsServerCount()
    {
        var servers = Substitute.For<IServerService>();
        servers.ListAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<RustServer>
            {
                new()
                {
                    Name = "A"
                },
                new()
                {
                    Name = "B"
                },
                new()
                {
                    Name = "C"
                }
            });
        var renderer = new InformationMessageRenderer(servers, Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Embed);
        Assert.Contains("3", payload.Embed!.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_HasLanguageSelectMenu()
    {
        var renderer = new SettingsMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Components);
        var selects = payload.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<SelectMenuComponent>();
        Assert.Contains(selects, s => s.CustomId == "workspace:settings:culture");
    }

    [Fact]
    public async Task ServerInfo_TitleIsServerName_AndEndpointShown()
    {
        var serverId = Guid.NewGuid();
        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId,
                GuildId = 1,
                Name = "Rustopia EU",
                Ip = "1.2.3.4",
                Port = 28015
            });
        var renderer = new ServerInfoMessageRenderer(servers, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Equal("Rustopia EU", payload.Embed!.Title);
        Assert.Contains("1.2.3.4", payload.Embed.Description, StringComparison.Ordinal);
    }
}
