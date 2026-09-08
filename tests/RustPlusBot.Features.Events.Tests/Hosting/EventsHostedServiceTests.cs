using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using ConnectionState = RustPlusBot.Domain.Connections.ConnectionState;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Tests.Fakes;
using RustPlusBot.Features.Events.Hosting;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Tests.Hosting;

/// <summary>
/// Exercises the four loops <see cref="EventsHostedService"/> owns — marker relay, rig relay, rig tick and
/// disconnect-clear — end to end over a real <see cref="InMemoryEventBus"/> and a real
/// <see cref="EventRelay"/>. The bus is wrapped so a test can wait for the loop's subscription to be live
/// before publishing: the bus does not replay, and the loops subscribe from inside a <c>Task.Run</c>.
/// </summary>
public sealed class EventsHostedServiceTests
{
    private const ulong Guild = 10UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Marker_deltas_published_on_the_bus_reach_the_events_channel()
    {
        await using var h = Harness.Create();
        var posted = h.SignalOnPost();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.Bus.WhenSubscribedAsync<MapMarkersChangedEvent>().WaitAsync(Patience);
            await h.Bus.PublishAsync(CargoAdded());
            await posted.Task.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Poster.Received(1).PostAsync(Harness.EventsChannel, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        Assert.Single(h.Stores.Store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
    }

    [Fact]
    public async Task Rig_events_published_on_the_bus_reach_the_events_channel()
    {
        await using var h = Harness.Create();
        var posted = h.SignalOnPost();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.Bus.WhenSubscribedAsync<RigStateChangedEvent>().WaitAsync(Patience);
            await h.Bus.PublishAsync(
                new RigStateChangedEvent(Guild, Server, RigKind.Large, RigEventKind.Activated, 1f, 2f, null));
            await posted.Task.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Poster.Received(1).PostAsync(Harness.EventsChannel, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_rig_tick_publishes_a_timed_crossing_and_the_rig_loop_relays_it()
    {
        // Nothing republishes a rig phase change: the crossing exists only because the tick loop keeps
        // advancing the store on its own interval. Cover the loop, not just TickOnceAsync.
        await using var h = Harness.Create(rigTick: TimeSpan.FromMilliseconds(10));
        h.Stores.RigStore.Apply(
            new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.Activated, 1f, 2f, null));
        h.Clock.UtcNow = h.Clock.UtcNow.Add(h.Options.Value.RigActiveWindow);
        var posted = h.SignalOnPost();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await posted.Task.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Contains(h.Bus.Published.OfType<RigStateChangedEvent>(),
            e => e.Kind == RigEventKind.CrateLootable && e.Rig == RigKind.Small);
    }

    [Fact]
    public async Task A_server_that_is_no_longer_connected_has_its_marker_and_rig_state_cleared()
    {
        await using var h = Harness.Create();
        h.ConnectionStore.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(_ => h.CountStatusReadAsync(State(ConnectionStatus.Unreachable)));
        await h.SeedStateAsync();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.Bus.WhenSubscribedAsync<ConnectionStatusChangedEvent>().WaitAsync(Patience);
            await h.PublishTwoStatusEventsAndWaitAsync();
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Empty(h.Stores.Store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
        Assert.Equal(RigStatus.Online, h.Stores.RigStore.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public async Task A_server_the_store_still_reports_as_connected_keeps_its_state()
    {
        // The bus is an unbounded queue, so a status event can be dequeued long after the status it
        // announced has been superseded. The store, not the event payload, decides — a reconnect that
        // arrived first must not have its live marker and rig state wiped by the stale disconnect behind it.
        await using var h = Harness.Create();
        h.ConnectionStore.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(_ => h.CountStatusReadAsync(State(ConnectionStatus.Connected)));
        await h.SeedStateAsync();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.Bus.WhenSubscribedAsync<ConnectionStatusChangedEvent>().WaitAsync(Patience);
            await h.PublishTwoStatusEventsAndWaitAsync();
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Single(h.Stores.Store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
        Assert.Equal(RigStatus.Active, h.Stores.RigStore.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public async Task An_unknown_server_is_cleared_rather_than_left_holding_stale_markers()
    {
        // No row at all means the server was removed while its markers were live; treat it as disconnected.
        await using var h = Harness.Create();
        h.ConnectionStore.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(_ => h.CountStatusReadAsync(null));
        await h.SeedStateAsync();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.Bus.WhenSubscribedAsync<ConnectionStatusChangedEvent>().WaitAsync(Patience);
            await h.PublishTwoStatusEventsAndWaitAsync();
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Empty(h.Stores.Store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
    }

    [Fact]
    public async Task One_failing_relay_costs_its_own_event_and_not_the_subscription()
    {
        // A Discord 5xx inside the relay used to escape the await-foreach and end the marker subscription
        // for the rest of the process: #events then went silent until the bot restarted.
        await using var h = Harness.Create();
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        h.Poster.PostAsync(Arg.Any<ulong>(), Arg.Any<Embed>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new TimeoutException("The operation has timed out.");
                }

                second.TrySetResult();
                return Task.CompletedTask;
            });

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.Bus.WhenSubscribedAsync<MapMarkersChangedEvent>().WaitAsync(Patience);
            await h.Bus.PublishAsync(CargoAdded());
            await h.Bus.PublishAsync(CargoAdded(2));
            await second.Task.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task StopAsync_joins_every_loop_and_completes()
    {
        await using var h = Harness.Create();
        await h.Service.StartAsync(CancellationToken.None);
        await h.Bus.WhenSubscribedAsync<MapMarkersChangedEvent>().WaitAsync(Patience);
        await h.Bus.WhenSubscribedAsync<RigStateChangedEvent>().WaitAsync(Patience);
        await h.Bus.WhenSubscribedAsync<ConnectionStatusChangedEvent>().WaitAsync(Patience);

        await h.Service.StopAsync(CancellationToken.None);

        // Every subscription has been torn down, so a later publish reaches nothing.
        await h.Bus.PublishAsync(CargoAdded());
        await h.Poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_subscription_that_ends_does_not_stop_the_host_from_shutting_down()
    {
        var bus = Substitute.For<IEventBus>();
        StubStreams(bus, static () => AsyncEnumerable.Empty<object>().Cast<object>());
        await using var h = Harness.Create(bus: bus);

        await h.Service.StartAsync(CancellationToken.None);
        var stop = h.Service.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_faulting_bus_ends_the_loops_without_faulting_the_host()
    {
        // A loop that ends because the stream itself broke must be contained: rethrowing it out of the
        // joined task would fail the host's shutdown on the way down.
        var fault = new InvalidOperationException("the subscription broke.");
        var bus = Substitute.For<IEventBus>();
        StubStreams(bus, () => throw fault);
        var ticked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA2012, VSTHRD110 // NSubstitute call specification, never executed as a real call.
        bus.When(b => b.PublishAsync(Arg.Any<RigStateChangedEvent>(), Arg.Any<CancellationToken>()))
#pragma warning restore CA2012, VSTHRD110
            .Do(_ =>
            {
                ticked.TrySetResult();
                throw fault;
            });

        await using var h = Harness.Create(rigTick: TimeSpan.FromMilliseconds(10), bus: bus);
        h.Stores.RigStore.Apply(
            new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.Activated, 1f, 2f, null));
        h.Clock.UtcNow = h.Clock.UtcNow.Add(h.Options.Value.RigActiveWindow);

        await h.Service.StartAsync(CancellationToken.None);
        await ticked.Task.WaitAsync(Patience);
        var stop = h.Service.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    /// <summary>Stubs every stream this service subscribes to with the same factory.</summary>
    /// <param name="bus">The substituted bus.</param>
    /// <param name="stream">Produces one element of the stream, or throws to fault it.</param>
    private static void StubStreams(IEventBus bus, Func<IAsyncEnumerable<object>> stream)
    {
        bus.SubscribeAsync<MapMarkersChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<MapMarkersChangedEvent>());
        bus.SubscribeAsync<RigStateChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<RigStateChangedEvent>());
        bus.SubscribeAsync<ConnectionStatusChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<ConnectionStatusChangedEvent>());
    }

    private static MapMarkersChangedEvent CargoAdded(ulong id = 1) =>
        new(Guild, Server, null, [new MapMarkerSnapshot(id, MarkerKind.CargoShip, 0f, 0f, null)], [], []);

    private static ConnectionState State(ConnectionStatus status) => new()
    {
        GuildId = Guild, RustServerId = Server, Status = status
    };

    /// <summary>Wires a real relay, real state stores and a real bus around the service under test.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        internal const ulong EventsChannel = 999UL;

        private readonly TaskCompletionSource _secondStatusRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _statusReads;

        public required SubscriptionAwareBus Bus { get; init; }

        public required MutableClock Clock { get; init; }

        public required IConnectionStore ConnectionStore { get; init; }

        public required IOptions<ConnectionOptions> Options { get; init; }

        public required IEventChannelPoster Poster { get; init; }

        public required ServiceProvider Provider { get; init; }

        public required EventRelay Relay { get; init; }

        public required EventsHostedService Service { get; init; }

        public required EventStores Stores { get; init; }

        public static Harness Create(TimeSpan? rigTick = null, IEventBus? bus = null)
        {
            var clock = new MutableClock();
            var options = Microsoft.Extensions.Options.Options.Create(new ConnectionOptions
            {
                RigTickInterval = rigTick ?? TimeSpan.FromHours(1)
            });

            var workspaceStore = Substitute.For<IWorkspaceStore>();
            workspaceStore.GetCultureAsync(Guild, Arg.Any<CancellationToken>()).Returns("en");
            var mapSettings = Substitute.For<IMapSettingsStore>();
            mapSettings.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(MapLayerSettings.AllOn);
            var connectionStore = Substitute.For<IConnectionStore>();

            var services = new ServiceCollection();
            services.AddScoped(_ => workspaceStore);
            services.AddScoped(_ => mapSettings);
            services.AddScoped(_ => connectionStore);
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var locator = Substitute.For<IEventChannelLocator>();
            locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((ulong?)EventsChannel);
            var poster = Substitute.For<IEventChannelPoster>();

            var sender = Substitute.For<IBotTeamChatSender>();

            var eventStateStore = new EventStateStore(clock);
            var rigStore = new RigStateStore(clock, options);
            var relay = new EventRelay(
                new MarkerEventClassifier(clock),
                eventStateStore,
                new EventEmbedRenderer(new ResxLocalizer()),
                new EventRelayChannels(locator, poster, sender),
                rigStore,
                scopeFactory);

            var subscriptionAwareBus = new SubscriptionAwareBus();
            var effectiveBus = bus ?? subscriptionAwareBus;
            var stores = new EventStores(eventStateStore, rigStore);
            return new Harness
            {
                Bus = subscriptionAwareBus,
                Clock = clock,
                ConnectionStore = connectionStore,
                Options = options,
                Poster = poster,
                Provider = provider,
                Relay = relay,
                Service = new EventsHostedService(effectiveBus, relay, stores, clock, options, scopeFactory,
                    NullLogger<EventsHostedService>.Instance),
                Stores = stores,
            };
        }

        public Task<ConnectionState?> CountStatusReadAsync(ConnectionState? state)
        {
            if (Interlocked.Increment(ref _statusReads) == 2)
            {
                _secondStatusRead.TrySetResult();
            }

            return Task.FromResult(state);
        }

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await Provider.DisposeAsync();
        }

        /// <summary>Completes once the relay has posted an embed, so assertions never race the loop.</summary>
        public TaskCompletionSource SignalOnPost()
        {
            var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Poster.PostAsync(Arg.Any<ulong>(), Arg.Any<Embed>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    posted.TrySetResult();
                    return Task.CompletedTask;
                });
            return posted;
        }

        /// <summary>
        /// Publishes two status events and waits for the second to be read. The consume loop is sequential,
        /// so reaching the second read proves the first event's handler ran to completion — which is what
        /// lets a test assert that nothing was cleared without racing the handler.
        /// </summary>
        public async Task PublishTwoStatusEventsAndWaitAsync()
        {
            await Bus.PublishAsync(new ConnectionStatusChangedEvent(Guild, Server, false, true));
            await Bus.PublishAsync(new ConnectionStatusChangedEvent(Guild, Server, false, true));
            await _secondStatusRead.Task.WaitAsync(Patience);
        }

        /// <summary>Puts a live cargo-ship marker and an active small rig into the stores.</summary>
        public async Task SeedStateAsync()
        {
            await Relay.RelayAsync(CargoAdded(), CancellationToken.None);
            Stores.RigStore.Apply(
                new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.Activated, 1f, 2f, null));
            Assert.Single(Stores.Store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
            Assert.Equal(RigStatus.Active, Stores.RigStore.Get(Guild, Server, RigKind.Small).Status);
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    }
}
