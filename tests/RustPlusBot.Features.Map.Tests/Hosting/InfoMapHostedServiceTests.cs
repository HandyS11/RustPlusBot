using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.RustMaps;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Map.Tests.Hosting;

public sealed class InfoMapHostedServiceTests
{
    private const ulong GuildA = 1UL;
    private const ulong GuildB = 2UL;
    private static readonly Guid ServerA = Guid.NewGuid();
    private static readonly Guid ServerB = Guid.NewGuid();
    private static readonly RustMapsMapKey Key = new(4000, 12345);

    private static IServiceScopeFactory ScopeFactory(IConnectionStore store) =>
        new ServiceCollection()
            .AddScoped(_ => store)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static IOptions<MapOptions> ShortPollOptions() => Options.Create(new MapOptions
    {
        RustMaps = new RustMapsOptions
        {
            GenerationPollInterval = TimeSpan.FromMilliseconds(10)
        }
    });

    [Fact]
    public async Task Ready_key_publishes_InfoMapReadyEvent_once_per_requester()
    {
        var coordinator = new RustMapsMapCoordinator();
        coordinator.Register(Key, GuildA, ServerA);
        coordinator.Register(Key, GuildB, ServerB);

        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(Key.Size, Key.Seed, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/plain.png",
                    ImageIconUrl = "https://img/icons.png",
                    Url = "https://rustmaps/x"
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        var bus = new InMemoryEventBus();
        var received = new List<InfoMapReadyEvent>();
        using var collectCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync<InfoMapReadyEvent>(collectCts.Token))
            {
                received.Add(evt);
            }
        });

        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(Substitute.For<IConnectionStore>()), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline && received.Count < 2)
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await collectCts.CancelAsync();
        }

        Assert.Equal(2, received.Count);
        Assert.Contains(received, e => e.GuildId == GuildA && e.ServerId == ServerA);
        Assert.Contains(received, e => e.GuildId == GuildB && e.ServerId == ServerB);
        Assert.Equal(RustMapsGenerationState.Ready, coordinator.Snapshot(Key).State);
        Assert.Equal("https://img/icons.png", coordinator.GetReady(Key.Size, Key.Seed)!.ImageUrl);
    }

    [Fact]
    public async Task Tick_registers_and_generates_connected_servers_without_any_connect_event()
    {
        // The ConnectionStatusChangedEvent is live-only and can be missed on startup (the connection may
        // publish it before this service subscribes). Registration must therefore come from the connection
        // store each tick, so a connected server still generates even if the event was never seen.
        var coordinator = new RustMapsMapCoordinator();
        var serverId = Guid.NewGuid();

        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(Key.Size, Key.Seed, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/plain.png",
                    ImageIconUrl = "https://img/icons.png",
                    Url = "https://rustmaps/x"
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(GuildA, serverId, Arg.Any<CancellationToken>())
            .Returns(new WorldSnapshot((uint)Key.Size, (uint)Key.Seed));

        var store = Substitute.For<IConnectionStore>();
        IReadOnlyList<(ulong GuildId, Guid ServerId)> connectable = [(GuildA, serverId)];
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>()).Returns(connectable);

        var bus = new InMemoryEventBus();
        var received = new List<InfoMapReadyEvent>();
        using var collectCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync<InfoMapReadyEvent>(collectCts.Token))
            {
                received.Add(evt);
            }
        });

        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(store), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline && received.Count < 1)
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await collectCts.CancelAsync();
        }

        // No ConnectionStatusChangedEvent was ever published — registration came from the store tick.
        Assert.Contains(received, e => e.GuildId == GuildA && e.ServerId == serverId);
        Assert.Equal(RustMapsGenerationState.Ready, coordinator.Snapshot(Key).State);
    }

    [Fact]
    public async Task Connect_registers_the_servers_world_key_with_the_coordinator()
    {
        // No tick should be needed for registration — the connection-status path resolves the world and
        // registers immediately, independent of the (here, effectively-never-firing) generation poll.
        var coordinator = new RustMapsMapCoordinator();
        var driver = new RustMapsGenerationDriver(
            Substitute.For<IRustMapsClient>(), coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var serverId = Guid.NewGuid();
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(GuildA, serverId, Arg.Any<CancellationToken>())
            .Returns(new WorldSnapshot((uint)Key.Size, (uint)Key.Seed));

        var connectionStore = Substitute.For<IConnectionStore>();
        connectionStore.GetStateAsync(GuildA, serverId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = GuildA, RustServerId = serverId, Status = ConnectionStatus.Connected
            });

        var bus = new InMemoryEventBus();
        var pollOptions = Options.Create(new MapOptions
        {
            RustMaps = new RustMapsOptions
            {
                GenerationPollInterval = TimeSpan.FromSeconds(30) // must not need to fire for this test to pass
            }
        });
        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, pollOptions,
            ScopeFactory(connectionStore), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline && coordinator.PendingKeys().Count == 0)
            {
                await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, serverId, true, false));
                await Task.Delay(20);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Contains(Key, coordinator.PendingKeys());
        Assert.Contains((GuildA, serverId), coordinator.Requesters(Key));
    }
}
