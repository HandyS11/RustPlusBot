using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Relaying;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class StorageMonitorStateRelayTests
{
    private const ulong Guild = 10UL;
    private static readonly Guid Server = Guid.NewGuid();

    private static Harness Create()
    {
        var store = Substitute.For<IStorageMonitorStore>();
        var connections = Substitute.For<IConnectionStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => connections);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();

        var locator = Substitute.For<IStorageMonitorChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(555UL);

        var poster = Substitute.For<IStorageMonitorChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)900UL);

        var names = Substitute.For<IItemNameResolver>();
        names.Resolve(Arg.Any<int>()).Returns(ci => "Item" + (int)ci[0]);
        var renderer = new StorageMonitorEmbedRenderer(new ResxLocalizer(), names);

        var relay = new StorageMonitorStateRelay(
            provider.GetRequiredService<IServiceScopeFactory>(), locator, poster, renderer);

        return new Harness(relay, store, poster, connections);
    }

    [Fact]
    public async Task HandleTriggeredAsync_UnmanagedEntity_DoesNothing()
    {
        var h = Create();
        h.Store.ExistsAsync(Guild, Server, 7UL, Arg.Any<CancellationToken>()).Returns(false);

        await h.Relay.HandleTriggeredAsync(
            new StorageMonitorTriggeredEvent(Guild, Server, 7UL,
                new StorageContentsSnapshot(24, null, null, [])), default);

        await h.Poster.DidNotReceive().EnsureAsync(
            Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleTriggeredAsync_ManagedEntity_PostsEmbed()
    {
        var h = Create();
        h.Store.ExistsAsync(Guild, Server, 7UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(Guild, Server, 7UL, Arg.Any<CancellationToken>())
            .Returns(new SmartStorageMonitor
            {
                GuildId = Guild,
                ServerId = Server,
                EntityId = 7UL,
                Name = "Tool Cupboard",
                MessageId = null,
            });

        await h.Relay.HandleTriggeredAsync(
            new StorageMonitorTriggeredEvent(Guild, Server, 7UL,
                new StorageContentsSnapshot(48, null, null, [new StorageItemSnapshot(100, 5, false)])), default);

        await h.Poster.Received(1).EnsureAsync(555UL, Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleConnectionStatusAsync_NotConnected_PostsUnreachable()
    {
        var h = Create();
        h.Connections.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = Guild, RustServerId = Server, Status = ConnectionStatus.Unreachable,
            });
        h.Store.ListByServerAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartStorageMonitor
                {
                    GuildId = Guild,
                    ServerId = Server,
                    EntityId = 7UL,
                    Name = "TC",
                    MessageId = null,
                },
            ]);

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(Guild, Server), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(555UL, Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleConnectionStatusAsync_Connected_DoesNothing()
    {
        var h = Create();
        h.Connections.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = Guild, RustServerId = Server, Status = ConnectionStatus.Connected,
            });

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(Guild, Server), CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReachabilityChangedAsync_ForeignEntity_DoesNothing()
    {
        var h = Create();
        h.Store.ExistsAsync(Guild, Server, 99UL, Arg.Any<CancellationToken>()).Returns(false);

        await h.Relay.HandleReachabilityChangedAsync(
            new DeviceReachabilityChangedEvent(Guild, Server, 99UL, DeviceReachability.Removed),
            CancellationToken.None);

        await h.Store.DidNotReceive().SetReachabilityAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<DeviceReachability>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReachabilityChangedAsync_OwnedEntity_PersistsAndRenders()
    {
        var h = Create();
        h.Store.ExistsAsync(Guild, Server, 42UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(Guild, Server, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartStorageMonitor
            {
                GuildId = Guild,
                ServerId = Server,
                EntityId = 42UL,
                Name = "TC",
                MessageId = 900UL,
                Reachability = DeviceReachability.Removed,
            });

        await h.Relay.HandleReachabilityChangedAsync(
            new DeviceReachabilityChangedEvent(Guild, Server, 42UL, DeviceReachability.Removed),
            CancellationToken.None);

        await h.Store.Received(1).SetReachabilityAsync(Guild, Server, 42UL, DeviceReachability.Removed,
            Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(555UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        StorageMonitorStateRelay Relay,
        IStorageMonitorStore Store,
        IStorageMonitorChannelPoster Poster,
        IConnectionStore Connections);
}
