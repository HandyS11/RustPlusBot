using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;
using DomainConnectionState = RustPlusBot.Domain.Connections.ConnectionState;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

public sealed class RendererTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly MessageRenderContext Global = new(1, null, "en");

    [Fact]
    public async Task Information_ShowsServerCount()
    {
        var servers = Substitute.For<IServerService>();
        servers.ListAsync(1, Arg.Any<CancellationToken>())
            .Returns(
            [
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
            ]);
        var renderer = new InformationMessageRenderer(servers, Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Embed);
        Assert.Contains("3", payload.Embed!.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_HasLanguageSelectMenu()
    {
        var settingsStore = Substitute.For<IWorkspaceStore>();
        settingsStore.GetPingEveryoneOnWipeAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(false);
        var renderer = new SettingsMessageRenderer(settingsStore, Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Components);
        var selects = payload.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<SelectMenuComponent>();
        Assert.Contains(selects, s => s.CustomId == "workspace:settings:culture");
    }

    [Fact]
    public async Task ServerInfo_Connected_ShowsPlayersTimeAndWipe()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

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

        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 187,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns([
                new()
                {
                    Id = credId,
                    GuildId = 1,
                    RustServerId = serverId,
                    OwnerUserId = 7,
                    SteamId = 76561198000000000UL,
                    Status = CredentialStatus.Active
                },
            ]);

        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(187, 200, 12, now.AddDays(-4).AddHours(-6)));
        query.GetTimeAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new ServerTimeSnapshot(14.0f, 7.0f, 20.0f));
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var values = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
        Assert.Contains("187 / 200", values, StringComparison.Ordinal);
        Assert.Contains("12 queued", values, StringComparison.Ordinal);
        Assert.Contains("14:00", values, StringComparison.Ordinal);
        Assert.Contains("4d 6h ago", values, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerInfo_NoLongerRendersSwapSelect()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();

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

        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 1,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns([
                new()
                {
                    Id = credId,
                    GuildId = 1,
                    RustServerId = serverId,
                    OwnerUserId = 7,
                    SteamId = 76561198000000000UL,
                    Status = CredentialStatus.Active
                },
            ]);

        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((ServerInfoSnapshot?)null);
        query.GetTimeAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((ServerTimeSnapshot?)null);
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((TeamInfoSnapshot?)null);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var selects = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<SelectMenuComponent>();
        Assert.Empty(selects);

        var buttons = payload.Components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == $"workspace:info:remove:{serverId}");
    }

    [Fact]
    public async Task ServerInfo_Disconnected_OmitsLiveFields()
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

        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId, GuildId = 1, Status = ConnectionStatus.Unreachable,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>()).Returns([]);

        var query = Substitute.For<IRustServerQuery>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        // Disconnected: never queries the socket, and shows only Status + Active player.
        await query.DidNotReceive().GetServerInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, payload.Embed!.Fields.Length);
    }

    [Fact]
    public async Task ServerInfo_NoCredentials_ShowsNoCredentialsAndNoCount()
    {
        var serverId = Guid.NewGuid();
        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId,
                GuildId = 1,
                Name = "S",
                Ip = "1.2.3.4",
                Port = 28015
            });
        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId, GuildId = 1, Status = ConnectionStatus.NoCredentials
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns([]);
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);
        var clock = Substitute.For<IClock>();

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Contains("No working credentials",
            string.Concat(payload.Embed!.Fields.Select(f => f.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerInfo_NullState_DefaultsToNoCredentials()
    {
        var serverId = Guid.NewGuid();
        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId,
                GuildId = 1,
                Name = "S",
                Ip = "1.2.3.4",
                Port = 28015
            });
        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns((DomainConnectionState?)null);
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns([]);
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);
        var clock = Substitute.For<IClock>();

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, clock, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Contains("No working credentials",
            string.Concat(payload.Embed!.Fields.Select(f => f.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_HasConnectAccountButton()
    {
        var renderer = new SetupMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Components);
        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == "workspace:setup:connect");
    }

    [Fact]
    public async Task Setup_HasDisconnectAccountButton()
    {
        var renderer = new SetupMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == "workspace:setup:disconnect");
    }
}
