using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Features.StorageMonitors.Hosting;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Relaying;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.StorageMonitors.Tests.Hosting;

public sealed class StorageMonitorsHostedServiceTests
{
    private const ulong Guild = 10UL;

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
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        // Relay collaborators
        var relayLocator = Substitute.For<IStorageMonitorChannelLocator>();
        relayLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(555UL);
        var relayPoster = Substitute.For<IStorageMonitorChannelPoster>();
        relayPoster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)900UL);
        var names = Substitute.For<IItemNameResolver>();
        names.Resolve(Arg.Any<int>()).Returns(ci => "Item" + (int)ci[0]);
        var renderer = new StorageMonitorEmbedRenderer(new ResxLocalizer(), names);
        var relay = new StorageMonitorStateRelay(scopeFactory, relayLocator, relayPoster, renderer,
            Substitute.For<IRustServerQuery>());

        // Coordinator collaborators
        var pairingLocator = Substitute.For<IStorageMonitorChannelLocator>();
        pairingLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(888UL);
        var pairingPoster = Substitute.For<IStorageMonitorChannelPoster>();
        pairingPoster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)901UL);
        var coordinator = new StorageMonitorPairingCoordinator(scopeFactory, pairingLocator, pairingPoster, renderer);

        var bus = new InMemoryEventBus();
        var service = new StorageMonitorsHostedService(
            bus,
            coordinator,
            relay,
            NullLogger<StorageMonitorsHostedService>.Instance);

        return new Harness(service, bus, store, connections, relayPoster, relayLocator, pairingLocator, pairingPoster);
    }

    [Fact]
    public async Task StorageMonitorPairedEvent_routes_to_coordinator_and_posts_prompt()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(Guild, serverId, 7UL, Arg.Any<CancellationToken>()).Returns(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.PairingPoster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IStorageMonitorChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new StorageMonitorPairedEvent(Guild, serverId, 7UL));
            await Task.Delay(20);
        }

        await h.PairingPoster.Received().EnsureAsync(
            888UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task StorageMonitorTriggeredEvent_managed_entity_routes_to_relay_and_posts_embed()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(Guild, serverId, 7UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(Guild, serverId, 7UL, Arg.Any<CancellationToken>())
            .Returns(new SmartStorageMonitor
            {
                GuildId = Guild,
                ServerId = serverId,
                EntityId = 7UL,
                Name = "TC",
                MessageId = null,
            });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IStorageMonitorChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new StorageMonitorTriggeredEvent(
                Guild, serverId, 7UL,
                new StorageContentsSnapshot(48, null, null, [new StorageItemSnapshot(100, 5, false)])));
            await Task.Delay(20);
        }

        await h.Poster.Received().EnsureAsync(
            555UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task ConnectionStatusChangedEvent_non_connected_routes_to_relay_and_rerenders()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Connections.GetStateAsync(Guild, serverId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = Guild, RustServerId = serverId, Status = ConnectionStatus.Unreachable,
            });
        h.Store.ListByServerAsync(Guild, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartStorageMonitor
                {
                    GuildId = Guild,
                    ServerId = serverId,
                    EntityId = 7UL,
                    Name = "TC",
                    MessageId = null,
                },
            ]);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IStorageMonitorChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new ConnectionStatusChangedEvent(Guild, serverId));
            await Task.Delay(20);
        }

        await h.Poster.Received().EnsureAsync(
            555UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task DeviceReachabilityChangedEvent_owned_entity_routes_to_relay_and_rerenders()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(Guild, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(Guild, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartStorageMonitor
            {
                GuildId = Guild,
                ServerId = serverId,
                EntityId = 42UL,
                Name = "TC",
                MessageId = 900UL,
                Reachability = DeviceReachability.Removed,
            });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IStorageMonitorChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(
                new DeviceReachabilityChangedEvent(Guild, serverId, 42UL, DeviceReachability.Removed));
            await Task.Delay(20);
        }

        await h.Store.Received().SetReachabilityAsync(
            Guild, serverId, 42UL, DeviceReachability.Removed, Arg.Any<CancellationToken>());
        await h.Poster.Received().EnsureAsync(
            555UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task TriggeredLoop_survives_a_faulting_relay_and_StopAsync_completes_cleanly()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(Guild, serverId, 7UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.When(s => s.GetAsync(Guild, serverId, 7UL, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("relay boom"));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Store.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IStorageMonitorStore.GetAsync)))
        {
            await h.Bus.PublishAsync(new StorageMonitorTriggeredEvent(
                Guild, serverId, 7UL,
                new StorageContentsSnapshot(48, null, null, [new StorageItemSnapshot(100, 5, false)])));
            await Task.Delay(20);
        }

        // The relay threw, causing the loop to fault and complete (LogTriggeredLoopFaulted). StopAsync joins the
        // faulted task cleanly — no rethrow. This is crash-isolation, not per-event resilience.
        await h.Store.Received().GetAsync(Guild, serverId, 7UL, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    private sealed record Harness(
        StorageMonitorsHostedService Service,
        InMemoryEventBus Bus,
        IStorageMonitorStore Store,
        IConnectionStore Connections,
        IStorageMonitorChannelPoster Poster,
        IStorageMonitorChannelLocator Locator,
        IStorageMonitorChannelLocator PairingLocator,
        IStorageMonitorChannelPoster PairingPoster);
}
