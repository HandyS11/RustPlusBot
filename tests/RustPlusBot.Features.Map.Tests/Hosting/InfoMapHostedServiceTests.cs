using System.Collections.Concurrent;
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

    /// <summary>
    /// A fake bus that records every published <see cref="InfoMapReadyEvent"/> and signals each arrival.
    /// Deterministic, unlike subscribing to the real <see cref="InMemoryEventBus"/> from a background task:
    /// that bus drops events published before the subscriber is active, which races the service's first tick.
    /// </summary>
    private static CapturingEventBus CapturingBus() => new();

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
        // Capture publishes off a substituted bus: the real InMemoryEventBus drops events published
        // before a subscriber is active, so collecting via a background subscription races the
        // service's first tick and flakes on slow CI runners.
        using var bus = CapturingBus();
        var received = bus.Received;

        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(Substitute.For<IConnectionStore>()), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.WaitForAsync(2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
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

        using var bus = CapturingBus();
        var received = bus.Received;

        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(store), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.WaitForAsync(1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
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
        // Signals the registration instead of polling for it: a poll loop needs the thread pool to take its
        // next turn, so on a saturated CI runner the whole deadline can elapse inside one Task.Delay.
        var registered = new SignallingCoordinator(coordinator);
        var service = new InfoMapHostedService(
            bus, registered, driver, query, pollOptions,
            ScopeFactory(connectionStore), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Published exactly once: StartAsync subscribes before it returns, so no event can be dropped.
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, serverId, true, false));
            await registered.Registered.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Contains(Key, coordinator.PendingKeys());
        Assert.Contains((GuildA, serverId), coordinator.Requesters(Key));
    }

    /// <summary>Forwards to a real coordinator and completes <see cref="Registered"/> on the first Register.</summary>
    /// <param name="inner">The coordinator that holds the real state.</param>
    private sealed class SignallingCoordinator(IRustMapsMapCoordinator inner) : IRustMapsMapCoordinator
    {
        private readonly TaskCompletionSource _registered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Registered => _registered.Task;

        public void Register(RustMapsMapKey key, ulong guildId, Guid serverId)
        {
            inner.Register(key, guildId, serverId);
            _registered.TrySetResult();
        }

        public RustMapsMapSnapshot Snapshot(RustMapsMapKey key) => inner.Snapshot(key);

        public bool TrySetGenerating(RustMapsMapKey key, string? mapId) => inner.TrySetGenerating(key, mapId);

        public void SetReady(RustMapsMapKey key, RustMapsReadyMap ready) => inner.SetReady(key, ready);

        public void SetFailed(RustMapsMapKey key) => inner.SetFailed(key);

        public void SetLimitReached(RustMapsMapKey key) => inner.SetLimitReached(key);

        public IReadOnlyList<RustMapsMapKey> PendingKeys() => inner.PendingKeys();

        public IReadOnlyList<(ulong Guild, Guid Server)> Requesters(RustMapsMapKey key) => inner.Requesters(key);
    }

    private sealed class CapturingEventBus : IEventBus, IDisposable
    {
        private readonly SemaphoreSlim _published = new(0);

        public ConcurrentQueue<InfoMapReadyEvent> Received { get; } = new();

        public void Dispose() => _published.Dispose();

        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            if (@event is InfoMapReadyEvent ready)
            {
                Received.Enqueue(ready);
                _published.Release();
            }

            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : notnull =>
            AsyncEnumerable.Empty<TEvent>(); // No live subscriptions needed: assertions read Received.

        /// <summary>
        /// Waits for <paramref name="count"/> events. Signal-based rather than a poll loop: polling needs the
        /// thread pool to take its next turn, so on a saturated runner a whole wall-clock deadline can elapse
        /// inside a single Task.Delay. A timeout here is not asserted on — the caller's assertions report it.
        /// </summary>
        /// <param name="count">How many events to wait for.</param>
        public async Task WaitForAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                for (var i = 0; i < count; i++)
                {
                    await _published.WaitAsync(timeout.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Timed out; the caller asserts on what actually arrived.
            }
        }
    }
}
