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
using RustPlusBot.Abstractions.Map;
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
    private static readonly int[] OneElement = [0];

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

    /// <summary>Ten monuments spread over the world, as RustMaps reports them (origin at the map centre).</summary>
    private static List<Monument> RenderMonuments() =>
    [
        .. Enumerable.Range(0, 10).Select(i => new Monument
        {
            Coordinates = new Coordinates((i * 300) - 1500, 1500 - (i * 300))
        })
    ];

    /// <summary>The same monuments as a server reports them over Rust+ (origin at the map corner).</summary>
    /// <param name="worldSize">The world size whose half-width shifts the origin.</param>
    private static IReadOnlyList<MonumentSnapshot> MatchingServerMonuments(int worldSize) =>
    [
        .. RenderMonuments().Select(m =>
            new MonumentSnapshot("token", m.Coordinates!.X + (worldSize / 2f), m.Coordinates.Y + (worldSize / 2f)))
    ];

    /// <summary>Monuments from some other world entirely — none of them sit on a rendered one.</summary>
    private static IReadOnlyList<MonumentSnapshot> ForeignServerMonuments() =>
        [.. Enumerable.Range(0, 10).Select(i => new MonumentSnapshot("token", 100f + (i * 137f), 90f + (i * 211f)))];

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
                    Url = "https://rustmaps/x",
                    Monuments = RenderMonuments()
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        query.GetMonumentsAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MatchingServerMonuments(Key.Size));
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
        var resolution = coordinator.Resolve(GuildA, ServerA, Key.Size, Key.Seed);
        Assert.Equal(InfoMapStatus.Verified, resolution.Status);
        Assert.Equal("https://img/icons.png", resolution.View!.ImageUrl);
    }

    [Fact]
    public async Task A_render_of_a_different_world_is_announced_as_a_mismatch()
    {
        // The live bug: a server on a pre-generated level reports a seed whose RustMaps render is a
        // different island. The render must be marked unusable for that server, not published as its map.
        var coordinator = new RustMapsMapCoordinator();
        coordinator.Register(Key, GuildA, ServerA);

        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(Key.Size, Key.Seed, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/plain.png",
                    ImageIconUrl = "https://img/icons.png",
                    Monuments = RenderMonuments()
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        query.GetMonumentsAsync(GuildA, ServerA, Arg.Any<CancellationToken>()).Returns(ForeignServerMonuments());

        using var bus = CapturingBus();
        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(Substitute.For<IConnectionStore>()), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.WaitForAsync(1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(InfoMapStatus.Mismatched, coordinator.Resolve(GuildA, ServerA, Key.Size, Key.Seed).Status);
    }

    [Fact]
    public async Task An_undecidable_render_is_not_announced_and_stays_pending()
    {
        // No monuments from the server (offline, or the map window has not resolved): nothing to judge on,
        // so the render is withheld rather than guessed at — and the next tick tries again.
        var coordinator = new RustMapsMapCoordinator();
        coordinator.Register(Key, GuildA, ServerA);

        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(Key.Size, Key.Seed, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/plain.png",
                    ImageIconUrl = "https://img/icons.png",
                    Monuments = RenderMonuments()
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        query.GetMonumentsAsync(GuildA, ServerA, Arg.Any<CancellationToken>())
            .Returns([]);

        using var bus = CapturingBus();
        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(Substitute.For<IConnectionStore>()), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Give the loop several poll intervals to prove the absence of a publish, not just its lateness.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Empty(bus.Received);
        Assert.Equal(InfoMapStatus.Pending, coordinator.Resolve(GuildA, ServerA, Key.Size, Key.Seed).Status);
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
                    Url = "https://rustmaps/x",
                    Monuments = RenderMonuments()
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(GuildA, serverId, Arg.Any<CancellationToken>())
            .Returns(new WorldSnapshot((uint)Key.Size, (uint)Key.Seed));
        query.GetMonumentsAsync(GuildA, serverId, Arg.Any<CancellationToken>())
            .Returns(MatchingServerMonuments(Key.Size));

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

    [Fact]
    public async Task A_status_event_for_a_server_the_store_no_longer_reports_as_connected_registers_nothing()
    {
        // The bus is an unbounded queue, so a connect event can be dequeued after the socket has already
        // dropped. Registering on the stale payload would spend RustMaps credits on a dead server.
        var coordinator = new RustMapsMapCoordinator();
        var query = Substitute.For<IRustServerQuery>();
        var connectionStore = Substitute.For<IConnectionStore>();
        var handled = SecondReadSignal(connectionStore, null);

        var bus = new InMemoryEventBus();
        using var service = NoTickService(bus, coordinator, query, connectionStore);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, ServerA, true, false));
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, ServerA, true, false));
            await handled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await query.DidNotReceive().GetWorldAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Empty(coordinator.PendingKeys());
    }

    [Fact]
    public async Task A_connected_server_whose_world_is_not_resolvable_yet_registers_nothing()
    {
        // The map window resolves asynchronously after connect; until it does there is no (size, seed) to
        // key a render on, and guessing one would render — and bill for — the wrong island.
        var coordinator = new RustMapsMapCoordinator();
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(GuildA, ServerA, Arg.Any<CancellationToken>()).Returns((WorldSnapshot?)null);
        var connectionStore = Substitute.For<IConnectionStore>();
        var handled = SecondReadSignal(connectionStore, new ConnectionState
        {
            GuildId = GuildA, RustServerId = ServerA, Status = ConnectionStatus.Connected
        });

        var bus = new InMemoryEventBus();
        using var service = NoTickService(bus, coordinator, query, connectionStore);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, ServerA, true, false));
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, ServerA, true, false));
            await handled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await query.Received().GetWorldAsync(GuildA, ServerA, Arg.Any<CancellationToken>());
        Assert.Empty(coordinator.PendingKeys());
    }

    [Fact]
    public async Task A_failing_status_read_costs_its_own_event_and_not_the_subscription()
    {
        // The connect burst is exactly when the database is busiest; one failed read must not cost every
        // later connect its RustMaps registration for the rest of the process.
        var coordinator = new RustMapsMapCoordinator();
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(GuildA, ServerA, Arg.Any<CancellationToken>())
            .Returns(new WorldSnapshot((uint)Key.Size, (uint)Key.Seed));

        var reads = 0;
        var connectionStore = Substitute.For<IConnectionStore>();
        connectionStore.GetStateAsync(GuildA, ServerA, Arg.Any<CancellationToken>())
            .Returns<ConnectionState?>(_ => Interlocked.Increment(ref reads) == 1
                ? throw new TimeoutException("database is locked")
                : new ConnectionState
                {
                    GuildId = GuildA, RustServerId = ServerA, Status = ConnectionStatus.Connected
                });

        var bus = new InMemoryEventBus();
        var registered = new SignallingCoordinator(coordinator);
        using var service = NoTickService(bus, registered, query, connectionStore);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, ServerA, true, false));
            await bus.PublishAsync(new ConnectionStatusChangedEvent(GuildA, ServerA, true, false));
            await registered.Registered.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Contains((GuildA, ServerA), coordinator.Requesters(Key));
    }

    [Fact]
    public async Task A_faulting_status_stream_ends_that_loop_without_faulting_the_host()
    {
        // A loop that ends because the stream itself broke must be contained: rethrowing it out of the
        // joined task would fail the host's shutdown on the way down.
        // The subscription is established synchronously inside StartAsync, so the fault has to surface on
        // enumeration — which is where a real broken stream would surface it too.
        var bus = Substitute.For<IEventBus>();
        bus.SubscribeAsync<ConnectionStatusChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => OneElement.ToAsyncEnumerable()
                .Select<int, ConnectionStatusChangedEvent>(
                    _ => throw new InvalidOperationException("the subscription broke.")));
        using var service = NoTickService(bus, new RustMapsMapCoordinator(), Substitute.For<IRustServerQuery>(),
            Substitute.For<IConnectionStore>());

        await service.StartAsync(CancellationToken.None);
        var stop = service.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_tick_that_cannot_reach_the_connection_store_does_not_fault_the_host()
    {
        var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(ulong GuildId, Guid ServerId)>>(_ =>
            {
                listed.TrySetResult();
                throw new TimeoutException("database is locked");
            });

        using var service = new InfoMapHostedService(
            new InMemoryEventBus(), new RustMapsMapCoordinator(),
            new RustMapsGenerationDriver(Substitute.For<IRustMapsClient>(), new RustMapsMapCoordinator(),
                NullLogger<RustMapsGenerationDriver>.Instance),
            Substitute.For<IRustServerQuery>(), ShortPollOptions(), ScopeFactory(store),
            NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await listed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var stop = service.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_server_whose_verdict_is_already_decided_is_not_announced_again()
    {
        // Announcing a second time would make the workspace reconciler re-render #info for a render whose
        // verdict has not changed, on every single tick for the rest of the wipe.
        var coordinator = new RustMapsMapCoordinator();
        coordinator.Register(Key, GuildA, ServerA);

        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(Key.Size, Key.Seed, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/plain.png",
                    ImageIconUrl = "https://img/icons.png",
                    Monuments = RenderMonuments()
                }, 200));
        var driver = new RustMapsGenerationDriver(client, coordinator, NullLogger<RustMapsGenerationDriver>.Instance);

        var query = Substitute.For<IRustServerQuery>();
        query.GetMonumentsAsync(GuildA, ServerA, Arg.Any<CancellationToken>())
            .Returns(MatchingServerMonuments(Key.Size));

        using var bus = CapturingBus();
        var service = new InfoMapHostedService(
            bus, coordinator, driver, query, ShortPollOptions(),
            ScopeFactory(Substitute.For<IConnectionStore>()), NullLogger<InfoMapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await bus.WaitForAsync(1);
            // Several more poll intervals pass with the verdict already recorded.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Single(bus.Received);
        Assert.Equal(RustMapsMapMatch.Match, coordinator.MatchFor(Key, GuildA, ServerA));
    }

    /// <summary>Builds a service whose generation poll is far enough out that only the status loop runs.</summary>
    /// <param name="bus">The event bus to consume status events from.</param>
    /// <param name="coordinator">The coordinator under observation.</param>
    /// <param name="query">The live query seam.</param>
    /// <param name="connectionStore">The store the handler re-reads the status from.</param>
    /// <returns>The configured service.</returns>
    private static InfoMapHostedService NoTickService(
        IEventBus bus,
        IRustMapsMapCoordinator coordinator,
        IRustServerQuery query,
        IConnectionStore connectionStore) =>
        new(bus, coordinator,
            new RustMapsGenerationDriver(Substitute.For<IRustMapsClient>(), coordinator,
                NullLogger<RustMapsGenerationDriver>.Instance),
            query,
            Options.Create(new MapOptions
            {
                RustMaps = new RustMapsOptions
                {
                    GenerationPollInterval = TimeSpan.FromMinutes(30)
                }
            }),
            ScopeFactory(connectionStore), NullLogger<InfoMapHostedService>.Instance);

    /// <summary>
    /// Stubs the status read and signals on its second call. The consume loop is sequential, so reaching the
    /// second read proves the first event's handler ran to completion — which is what lets a test assert
    /// that nothing was registered without racing the handler.
    /// </summary>
    /// <param name="store">The store to stub.</param>
    /// <param name="state">The state every read returns.</param>
    /// <returns>The signal completed on the second read.</returns>
    private static TaskCompletionSource SecondReadSignal(IConnectionStore store, ConnectionState? state)
    {
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        store.GetStateAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref reads) == 2)
                {
                    handled.TrySetResult();
                }

                return Task.FromResult(state);
            });
        return handled;
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

        public void SetMatch(RustMapsMapKey key, ulong guildId, Guid serverId, RustMapsMapMatch match) =>
            inner.SetMatch(key, guildId, serverId, match);

        public RustMapsMapMatch MatchFor(RustMapsMapKey key, ulong guildId, Guid serverId) =>
            inner.MatchFor(key, guildId, serverId);

        public IReadOnlyList<RustMapsMapKey> ReadyKeys() => inner.ReadyKeys();

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