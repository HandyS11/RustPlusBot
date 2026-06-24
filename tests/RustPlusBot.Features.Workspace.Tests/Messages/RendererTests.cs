using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;
using DomainConnectionState = RustPlusBot.Domain.Connections.ConnectionState;

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
        var renderer = new SettingsMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Components);
        var selects = payload.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<SelectMenuComponent>();
        Assert.Contains(selects, s => s.CustomId == "workspace:settings:culture");
    }

    [Fact]
    public async Task ServerInfo_Connected_ShowsStatusActivePlayerAndCount_AndSwapSelect()
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
                PlayerCount = 12,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
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
        query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Contains("Rustopia EU", payload.Embed!.Title, StringComparison.Ordinal);
        Assert.Contains("12", string.Concat(payload.Embed.Fields.Select(f => f.Value)), StringComparison.Ordinal);
        Assert.Contains("76561198000000000", string.Concat(payload.Embed.Fields.Select(f => f.Value)),
            StringComparison.Ordinal);
        var selects = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<SelectMenuComponent>();
        Assert.Contains(selects, s => s.CustomId == $"workspace:info:swap:{serverId}");
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

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

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

        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

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

    [Fact]
    public async Task ServerInfo_HasRemoveServerButton()
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
        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == $"workspace:info:remove:{serverId}");
    }

    [Fact]
    public async Task ServerInfo_SwapSelectAndRemoveButton_AreInSeparateActionRows()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();
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
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 1,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new()
                {
                    Id = credId,
                    GuildId = 1,
                    RustServerId = serverId,
                    OwnerUserId = 7,
                    SteamId = 5UL,
                    Status = CredentialStatus.Active
                },
            ]);
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);
        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var rows = payload.Components!.Components.OfType<ActionRowComponent>().ToList();
        // Discord rejects an action row that mixes a select menu with buttons.
        Assert.DoesNotContain(rows, r =>
            r.Components.OfType<SelectMenuComponent>().Any() && r.Components.OfType<ButtonComponent>().Any());
        Assert.Contains(rows, r => r.Components.OfType<SelectMenuComponent>().Any());
        Assert.Contains(rows, r => r.Components.OfType<ButtonComponent>().Any());
    }

    [Fact]
    public async Task ServerInfo_Connected_WithTeam_ShowsTeamSummary()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();
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
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 5,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new()
                {
                    Id = credId,
                    GuildId = 1,
                    RustServerId = serverId,
                    OwnerUserId = 7,
                    SteamId = 5UL,
                    Status = CredentialStatus.Active
                },
            ]);
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(
                10UL,
                [
                    new TeamMemberSnapshot(10UL, "alice", 0f, 0f, true, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                    new TeamMemberSnapshot(20UL, "bob", 0f, 0f, false, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                ]));
        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var body = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
        Assert.Contains("1/2 online", body, StringComparison.Ordinal);
        Assert.Contains("alice", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerInfo_Connected_LeaderHasEmptyName_FallsBackToSteamId()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();
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
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 1,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new()
                {
                    Id = credId,
                    GuildId = 1,
                    RustServerId = serverId,
                    OwnerUserId = 7,
                    SteamId = 5UL,
                    Status = CredentialStatus.Active
                },
            ]);
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(
                10UL,
                [
                    // Leader is present but the API reported no display name.
                    new TeamMemberSnapshot(10UL, string.Empty, 0f, 0f, true, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                ]));
        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var body = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
        Assert.Contains("leader 10", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerInfo_Connected_NullTeam_OmitsTeamSummary()
    {
        var serverId = Guid.NewGuid();
        var credId = Guid.NewGuid();
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
                RustServerId = serverId,
                GuildId = 1,
                ActiveCredentialId = credId,
                Status = ConnectionStatus.Connected,
                PlayerCount = 5,
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new()
                {
                    Id = credId,
                    GuildId = 1,
                    RustServerId = serverId,
                    OwnerUserId = 7,
                    SteamId = 5UL,
                    Status = CredentialStatus.Active
                },
            ]);
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);
        var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var teamLabel = Loc.Get("server.info.team.label", "en");
        Assert.DoesNotContain(payload.Embed!.Fields, f => f.Name == teamLabel);
    }
}
