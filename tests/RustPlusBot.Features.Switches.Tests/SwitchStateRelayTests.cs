using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Relaying;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Switches;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchStateRelayTests
{
    private static Harness Create()
    {
        var store = Substitute.For<ISwitchStore>();
        var connections = Substitute.For<IConnectionStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => connections);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();

        var locator = Substitute.For<ISwitchChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(777UL);
        var poster = Substitute.For<ISwitchChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)900UL);
        var renderer = new SwitchEmbedRenderer(new ResxLocalizer());

        var relay = new SwitchStateRelay(provider.GetRequiredService<IServiceScopeFactory>(), locator, poster,
            renderer);
        return new Harness(relay, store, poster, connections);
    }

    [Fact]
    public async Task StateChanged_updates_store_and_rerenders()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartSwitch
            {
                GuildId = 10UL,
                ServerId = serverId,
                EntityId = 42UL,
                Name = "G",
                MessageId = 900UL
            });

        await h.Relay.HandleStateChangedAsync(
            new SwitchStateChangedEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Store.Received(1).UpdateStateAsync(10UL, serverId, 42UL, true, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectionStatus_not_connected_marks_switches_unreachable()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Connections.GetStateAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = 10UL, RustServerId = serverId, Status = ConnectionStatus.Unreachable
            });
        h.Store.ListByServerAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartSwitch
                {
                    GuildId = 10UL,
                    ServerId = serverId,
                    EntityId = 42UL,
                    Name = "G",
                    MessageId = 900UL
                }
            ]);

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(10UL, serverId), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectionStatus_connected_does_nothing()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Connections.GetStateAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = 10UL, RustServerId = serverId, Status = ConnectionStatus.Connected
            });

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(10UL, serverId), CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeviceTriggered_managed_switch_updates_store_and_rerenders()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartSwitch
            {
                GuildId = 10UL,
                ServerId = serverId,
                EntityId = 42UL,
                Name = "G",
                MessageId = 900UL
            });

        await h.Relay.HandleDeviceTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Store.Received(1).UpdateStateAsync(10UL, serverId, 42UL, true, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeviceTriggered_non_switch_id_is_ignored()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 99UL, Arg.Any<CancellationToken>()).Returns(false);

        await h.Relay.HandleDeviceTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 99UL, IsActive: true), CancellationToken.None);

        await h.Store.DidNotReceive().UpdateStateAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        SwitchStateRelay Relay,
        ISwitchStore Store,
        ISwitchChannelPoster Poster,
        IConnectionStore Connections);
}
