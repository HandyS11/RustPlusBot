using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustMapsApi.V4.Assets;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Assets;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Map;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ConnectionState = RustPlusBot.Domain.Connections.ConnectionState;

namespace RustPlusBot.Features.Map.Tests.Hosting;

/// <summary>
/// Covers what <see cref="MapHostedService"/> does with a refresh once one is triggered: the throttle that
/// stops the surfaces double-posting, the render-and-post path itself, the periodic backstop tick, and the
/// disconnect cleanup.
/// </summary>
public sealed class MapRefreshTests
{
    private const ulong Guild = 1UL;
    private const ulong MapChannel = 777UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly MapDimensions Dims = new(2000, 2000, 100, 4000);

    [Fact]
    public async Task A_marker_change_renders_the_map_and_posts_it_to_the_servers_map_channel()
    {
        using var h = Harness.Create();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilAsync(() => new MapMarkersChangedEvent(Guild, Server, null, [], [], []),
                h.FirstPost);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        // A real render went out to the located channel: non-empty PNG bytes, not a placeholder.
        await h.Poster.Received()
            .PostAsync(MapChannel, Arg.Is<byte[]>(b => b.Length > 0), Arg.Any<MapLegend?>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_second_change_inside_the_refresh_window_is_throttled_to_a_single_post()
    {
        // Markers, the settings toggles, the connect hook and the tick all feed the same repaint. Without
        // the per-server throttle a busy server would have #map re-uploaded several times a second.
        using var h = Harness.Create(FixedClock());

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            // Every refresh attempt reads the clock exactly once (inside the throttle), so waiting for the
            // third read proves at least three attempts were made — all but one of them must be dropped.
            await h.PublishUntilAsync(() => new MapMarkersChangedEvent(Guild, Server, null, [], [], []),
                h.Clock.WhenReadAtLeastAsync(3));
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Poster.Received(1)
            .PostAsync(MapChannel, Arg.Any<byte[]>(), Arg.Any<MapLegend?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_settings_toggle_repaints_immediately_rather_than_waiting_for_the_next_tick()
    {
        using var h = Harness.Create();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilAsync(() => new MapSettingsChangedEvent(Guild, Server), h.FirstPost);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Poster.Received()
            .PostAsync(MapChannel, Arg.Any<byte[]>(), Arg.Any<MapLegend?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_connected_server_keeps_being_repainted_by_the_periodic_tick_with_no_further_events()
    {
        // A missed marker delta would otherwise freeze #map until the next connect. The tick is the backstop:
        // once a server is known-connected it must keep repainting on its own.
        using var h = Harness.Create(tick: TimeSpan.FromMilliseconds(20));
        h.ConnectionStore.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Connected());

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilAsync(() => new ConnectionStatusChangedEvent(Guild, Server, true, false),
                h.FirstPost);

            // Nothing is published from here on: any further post can only have come from the tick.
            await h.PostCountReachesAsync(3).WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.True(h.Posts >= 3, $"the periodic tick stopped repainting (posts: {h.Posts})");
    }

    [Fact]
    public async Task A_server_that_is_no_longer_connected_drops_out_of_the_tick_and_loses_its_cached_base_map()
    {
        // The base map is static per wipe, so it is cached; a disconnect is the signal that the next
        // connection may be a different world and the cached tile must not be reused.
        using var h = Harness.Create(tick: TimeSpan.FromMilliseconds(20));
        h.ConnectionStore.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ConnectionState?>(null));

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            // Prime the cache through a normal repaint.
            await h.PublishUntilAsync(() => new MapMarkersChangedEvent(Guild, Server, null, [], [], []),
                h.FirstPost);
            Assert.Equal(1, h.BaseMapFetches);

            await h.PublishUntilAsync(() => new ConnectionStatusChangedEvent(Guild, Server, false, true),
                h.WhenStatusHandled);

            await h.PublishUntilAsync(() => new MapMarkersChangedEvent(Guild, Server, null, [], [], []),
                h.PostCountReachesAsync(2));
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, h.BaseMapFetches);
    }

    [Fact]
    public async Task A_repaint_that_throws_inside_the_tick_costs_that_repaint_and_not_the_tick()
    {
        // The tick is the only thing keeping #map current for a server whose deltas were missed. One
        // Discord or Rust+ failure inside it must not end the loop for the rest of the process.
        using var h = Harness.Create(tick: TimeSpan.FromMilliseconds(20),
            locatorFault: new TimeoutException("Discord did not answer."));
        h.ConnectionStore.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Connected());

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilAsync(() => new ConnectionStatusChangedEvent(Guild, Server, true, false),
                h.LocatorFaultsReachAsync(1));

            // Nothing is published from here on: the later failures can only come from the tick.
            await h.LocatorFaultsReachAsync(3).WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.True(h.LocatorFaults >= 3, $"the tick stopped after a failed repaint (failures: {h.LocatorFaults})");
    }

    [Fact]
    public async Task A_server_whose_base_map_has_not_arrived_yet_posts_nothing()
    {
        // Posting a marker-only overlay with no map under it would replace the last good image with
        // something unreadable; waiting is the right answer.
        using var h = Harness.Create(baseMapAvailable: false);

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilAsync(() => new MapMarkersChangedEvent(Guild, Server, null, [], [], []),
                h.Clock.WhenReadAtLeastAsync(2));
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.True(h.BaseMapFetches >= 1, "the refresh never reached the composer");
        await h.Poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<byte[]>(), Arg.Any<MapLegend?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_subscription_that_ends_does_not_stop_the_host_from_shutting_down()
    {
        var bus = Substitute.For<IEventBus>();
        StubStreams(bus, static () => AsyncEnumerable.Empty<object>());
        using var h = Harness.Create(bus: bus);

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
        var bus = Substitute.For<IEventBus>();
        StubStreams(bus, static () => throw new InvalidOperationException("the subscription broke."));
        using var h = Harness.Create(bus: bus);

        await h.Service.StartAsync(CancellationToken.None);
        var stop = h.Service.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    /// <summary>Stubs every stream this service subscribes to with the same factory.</summary>
    /// <param name="bus">The substituted bus.</param>
    /// <param name="stream">Produces the stream, or throws to fault it.</param>
    private static void StubStreams(IEventBus bus, Func<IAsyncEnumerable<object>> stream)
    {
        bus.SubscribeAsync<MapMarkersChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<MapMarkersChangedEvent>());
        bus.SubscribeAsync<MapSettingsChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<MapSettingsChangedEvent>());
        bus.SubscribeAsync<ConnectionStatusChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<ConnectionStatusChangedEvent>());
    }

    private static ConnectionState Connected() => new()
    {
        GuildId = Guild, RustServerId = Server, Status = ConnectionStatus.Connected
    };

    private static CountingClock FixedClock() => new(TimeSpan.Zero);

    /// <summary>Completes the waiters whose target count has been reached.</summary>
    /// <param name="targets">The registered (target, signal) pairs.</param>
    /// <param name="reached">The count reached so far.</param>
    private static void ReleaseReached(List<(int Count, TaskCompletionSource Tcs)> targets, int reached)
    {
        lock (targets)
        {
            foreach (var signal in targets.Where(t => t.Count <= reached).Select(t => t.Tcs))
            {
                signal.TrySetResult();
            }
        }
    }

    /// <summary>Registers a signal that completes once <paramref name="count"/> has been reached.</summary>
    /// <param name="targets">The registered (target, signal) pairs.</param>
    /// <param name="count">The count to wait for.</param>
    /// <param name="reached">The count reached so far.</param>
    /// <returns>A task that completes when the count is reached.</returns>
    private static Task WaitForCountAsync(List<(int Count, TaskCompletionSource Tcs)> targets, int count, int reached)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (targets)
        {
            targets.Add((count, tcs));
        }

        ReleaseReached(targets, reached);
        return tcs.Task;
    }

    /// <summary>
    /// A clock that steps by a fixed amount per read and counts its reads. The throttle reads it exactly
    /// once per refresh attempt, so the read count is a deterministic fence on "N attempts have happened" —
    /// including the attempts the throttle rejects, which produce no other observable effect at all.
    /// </summary>
    /// <param name="step">How far the clock advances between reads; zero pins it.</param>
    private sealed class CountingClock(TimeSpan step) : IClock
    {
        private readonly List<(int Count, TaskCompletionSource Tcs)> _targets = [];
        private int _reads;

        public DateTimeOffset UtcNow
        {
            get
            {
                var reads = Interlocked.Increment(ref _reads);
                ReleaseReached(_targets, reads);
                return DateTimeOffset.UnixEpoch + (step * (reads - 1));
            }
        }

        /// <summary>Completes once the clock has been read at least <paramref name="count"/> times.</summary>
        /// <param name="count">The read count to wait for.</param>
        /// <returns>A task that completes when the clock has been read that many times.</returns>
        public Task WhenReadAtLeastAsync(int count) => WaitForCountAsync(_targets, count, Volatile.Read(ref _reads));
    }

    private sealed class FakeBaseMapSource(byte[]? jpeg, Action onFetch) : IBaseMapSource
    {
        public Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
        {
            onFetch();
            return Task.FromResult(jpeg is null
                ? null
                : new BaseMapImage(jpeg, (int)Dims.Width, (int)Dims.Height, Dims.OceanMargin));
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly List<(int Count, TaskCompletionSource Tcs)> _locatorTargets = [];
        private readonly List<(int Count, TaskCompletionSource Tcs)> _postTargets = [];
        private readonly TaskCompletionSource _statusHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _baseMapFetches;
        private int _locatorFaults;
        private int _posts;
        private int _statusReads;

        public int BaseMapFetches => Volatile.Read(ref _baseMapFetches);

        public int LocatorFaults => Volatile.Read(ref _locatorFaults);

        public required CountingClock Clock { get; init; }

        public required IConnectionStore ConnectionStore { get; init; }

        public Task FirstPost => PostCountReachesAsync(1);

        public required IMapChannelPoster Poster { get; init; }

        public int Posts => Volatile.Read(ref _posts);

        public required ServiceProvider Provider { get; init; }

        public MapHostedService Service { get; private set; } = null!;

        public required InMemoryEventBus Bus { get; init; }

        public Task WhenStatusHandled => _statusHandled.Task;

        public void Dispose()
        {
            Service.Dispose();
            Provider.Dispose();
        }

        public static Harness Create(
            CountingClock? clock = null,
            TimeSpan? tick = null,
            IEventBus? bus = null,
            bool baseMapAvailable = true,
            Exception? locatorFault = null)
        {
            var harnessClock = clock ?? new CountingClock(TimeSpan.FromHours(1));
            var settings = Substitute.For<IMapSettingsStore>();
            settings.GetAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(MapLayerSettings.AllOn);
            var connectionStore = Substitute.For<IConnectionStore>();

            var services = new ServiceCollection();
            services.AddScoped(_ => settings);
            services.AddScoped(_ => connectionStore);
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var locator = Substitute.For<IMapChannelLocator>();

            var query = Substitute.For<IRustServerQuery>();
            query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Dims);

            var inProcessBus = new InMemoryEventBus();
            var harness = new Harness
            {
                Bus = inProcessBus,
                Clock = harnessClock,
                ConnectionStore = connectionStore,
                Poster = Substitute.For<IMapChannelPoster>(),
                Provider = provider,
            };

            // One cache instance for both the composer and the service: the service clears the very cache
            // the composer reads, which is how a disconnect forces the next base map to be re-fetched.
            locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>())
                .Returns(_ => locatorFault is null
                    ? Task.FromResult((ulong?)MapChannel)
                    : throw harness.CountLocatorFault(locatorFault));
            var cache = new BaseMapCache([
                new FakeBaseMapSource(baseMapAvailable ? BaseJpeg() : null, harness.OnBaseMapFetch)
            ]);
            var composer = new MapComposer(cache, Substitute.For<IEventState>(), Rigs(), query,
                new MapRenderer(new MonumentIconSource(new MonumentAssetSource(),
                    NullLogger<MonumentIconSource>.Instance)),
                scopeFactory);

            harness.Poster.PostAsync(Arg.Any<ulong>(), Arg.Any<byte[]>(), Arg.Any<MapLegend?>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => harness.OnPostAsync());
            connectionStore.When(s => s.GetStateAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()))
                .Do(_ => harness.OnStatusRead());

            harness.Service = new MapHostedService(bus ?? inProcessBus,
                new MapPipeline(composer, cache, locator, harness.Poster), harnessClock,
                Options.Create(new MapOptions
                {
                    MapRefreshInterval = tick ?? TimeSpan.FromMinutes(30)
                }),
                scopeFactory, NullLogger<MapHostedService>.Instance);
            return harness;
        }

        public Task PostCountReachesAsync(int count) => WaitForCountAsync(_postTargets, count, Posts);

        /// <summary>Completes once the locator has failed <paramref name="count"/> times.</summary>
        /// <param name="count">How many failed lookups to wait for.</param>
        /// <returns>A task that completes when that many lookups have failed.</returns>
        public Task LocatorFaultsReachAsync(int count) => WaitForCountAsync(_locatorTargets, count, LocatorFaults);

        /// <summary>Counts one locator failure and returns the exception to throw.</summary>
        /// <param name="fault">The exception the locator reports.</param>
        /// <returns>The same exception, for the caller to throw.</returns>
        public Exception CountLocatorFault(Exception fault)
        {
            ReleaseReached(_locatorTargets, Interlocked.Increment(ref _locatorFaults));
            return fault;
        }

        /// <summary>
        /// Republishes until <paramref name="until"/> completes. The bus does not replay and the loops
        /// subscribe from inside a <c>Task.Run</c>, so the first publish can land before anyone is listening.
        /// </summary>
        /// <typeparam name="TEvent">The event type being published.</typeparam>
        /// <param name="make">Builds the event to publish.</param>
        /// <param name="until">The signal that the service has done the work under test.</param>
        public async Task PublishUntilAsync<TEvent>(Func<TEvent> make, Task until)
            where TEvent : notnull
        {
            using var deadline = new CancellationTokenSource(Patience);
            while (!until.IsCompleted && !deadline.IsCancellationRequested)
            {
                await Bus.PublishAsync(make());
                await Task.WhenAny(until, Task.Delay(20, deadline.Token));
            }

            await until.WaitAsync(Patience);
        }

        private static byte[] BaseJpeg()
        {
            using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms);
            return ms.ToArray();
        }

        private static IRigState Rigs()
        {
            var rigs = Substitute.For<IRigState>();
            rigs.Get(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<RigKind>())
                .Returns(new RigState(RigStatus.Online, null));
            return rigs;
        }

        private void OnBaseMapFetch() => Interlocked.Increment(ref _baseMapFetches);

        private Task OnPostAsync()
        {
            ReleaseReached(_postTargets, Interlocked.Increment(ref _posts));
            return Task.CompletedTask;
        }

        private void OnStatusRead()
        {
            // The consume loop is sequential, so a second read proves the first status event's handler ran
            // to completion — which is what lets the assertions below not race it.
            if (Interlocked.Increment(ref _statusReads) == 2)
            {
                _statusHandled.TrySetResult();
            }
        }
    }
}
