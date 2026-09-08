using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.Vending.Hosting;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Posting;
using RustPlusBot.Features.Vending.Relaying;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Tests.Hosting;

/// <summary>Unit tests for <see cref="VendingHostedService"/>.</summary>
public sealed class VendingHostedServiceTests
{
    private const ulong Guild = 42UL;
    private const uint WorldSize = 4000;
    private const int PipeId = 69511070;
    private const int Scrap = -932201673;

    private static readonly Guid Poison = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid Healthy = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static VendingMachineSnapshot Machine() =>
        new(1UL, 500f, 2900f, "Shop", false, [new VendingOfferSnapshot(PipeId, false, 1, Scrap, false, 10, 5)]);

    private static VendingMachinesObservedEvent Observed(Guid serverId) =>
        new(Guild, serverId, WorldSize, [Machine()]);

    private static ServerWipedEvent Wiped(Guid serverId) =>
        new(Guild, serverId, null, DateTimeOffset.UnixEpoch, 1U, WorldSize);

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), because);
    }

    [Fact]
    public async Task ObservedEvents_ReachTheRelayAndBecomeSearchable()
    {
        var h = Harness.Create();
        await using var _ = h;
        await h.Service.StartAsync(CancellationToken.None);

        // No delay before publishing: StartAsync subscribes synchronously precisely so the events
        // published between start-up and the loop tasks being scheduled are not dropped.
        await h.Bus.PublishAsync(Observed(Healthy));

        await WaitForAsync(() => h.Index.HasData(Guild, Healthy), "the observed event never reached the relay");
        var offer = Assert.Single(h.Index.Search(Guild, Healthy, PipeId, MapGridStyle.InGame));
        Assert.Equal(10, offer.CostPerOrder);
    }

    [Fact]
    public async Task AFailingObservedHandler_CostsOneEventNotTheLoop()
    {
        // The relay loop is the only thing that ever refreshes the search index or reconciles #vending.
        // One transient failure ending it would leave the feature silently dead until the host restarts.
        var h = Harness.Create();
        await using var _ = h;
        h.Locator.GetChannelIdAsync(Guild, Poison, Arg.Any<CancellationToken>())
            .Returns<ulong?>(__ => throw new InvalidOperationException("transient locator failure"));
        await h.Service.StartAsync(CancellationToken.None);

        await h.Bus.PublishAsync(Observed(Poison));
        await h.Bus.PublishAsync(Observed(Healthy));

        // The loop is sequential, so the healthy server can only have been indexed by a loop that
        // survived the poison event ahead of it.
        await WaitForAsync(() => h.Index.HasData(Guild, Healthy), "the loop died on the failing event");
    }

    [Fact]
    public async Task ConnectionStatusEvents_ReachTheRelayAndDropTheIndex()
    {
        var h = Harness.Create();
        await using var _ = h;
        h.Index.Replace(Guild, Healthy, WorldSize, [Machine()]);
        await h.Service.StartAsync(CancellationToken.None);

        await h.Bus.PublishAsync(new ConnectionStatusChangedEvent(Guild, Healthy, false, true));

        await WaitForAsync(() => !h.Index.HasData(Guild, Healthy),
            "the disconnect never reached the relay, so a dead server's prices stayed searchable");
    }

    [Fact]
    public async Task ServerWipedEvents_ReachThePurger()
    {
        var h = Harness.Create();
        await using var _ = h;
        var purged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Store.When(s => s.PurgeGridsAsync(Guild, Healthy, Arg.Any<CancellationToken>()))
            .Do(__ => purged.TrySetResult());
        await h.Service.StartAsync(CancellationToken.None);

        await h.Bus.PublishAsync(Wiped(Healthy));

        await WaitForAsync(() => purged.Task.IsCompleted, "the wipe never reached the purger");
        await h.Store.Received(1).PurgeGridsAsync(Guild, Healthy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingWipeHandler_CostsOneEventNotTheLoop()
    {
        var h = Harness.Create();
        await using var _ = h;
        h.Store.ListNotificationsAsync(Guild, Poison, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<VendingNotification>>(__ =>
                throw new InvalidOperationException("transient store failure"));
        var purged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Store.When(s => s.PurgeGridsAsync(Guild, Healthy, Arg.Any<CancellationToken>()))
            .Do(__ => purged.TrySetResult());
        await h.Service.StartAsync(CancellationToken.None);

        await h.Bus.PublishAsync(Wiped(Poison));
        await h.Bus.PublishAsync(Wiped(Healthy));

        await WaitForAsync(() => purged.Task.IsCompleted, "the wipe loop died on the failing event");
    }

    [Fact]
    public async Task StopAsync_JoinsTheLoopsRatherThanReturningMidHandler()
    {
        var h = Harness.Create();
        await using var _ = h;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        h.Locator.GetChannelIdAsync(Guild, Healthy, Arg.Any<CancellationToken>())
            .Returns<Task<ulong?>>(__ => GateAsync());
        await h.Service.StartAsync(CancellationToken.None);

        async Task<ulong?> GateAsync()
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            finished = true;
            return null;
        }

        await h.Bus.PublishAsync(Observed(Healthy));
        await entered.Task; // The relay loop is now inside the handler and cannot finish on its own.

        var stop = h.Service.StopAsync(CancellationToken.None);

        // Cancelling alone cannot end a loop that is mid-handler, so a StopAsync that joins its loop
        // tasks can never complete here however long we wait — which is what makes waiting sound rather
        // than a timing assumption. One that did not join would return as soon as the cancel landed.
        await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.False(stop.IsCompleted, "StopAsync returned while a handler was still in flight");

        release.SetResult();
        await stop;

        Assert.True(finished, "StopAsync returned before the in-flight handler had finished");
    }

    [Fact]
    public async Task AFaultingSubscriptionStream_IsLoggedByEveryLoopAndDoesNotCrashTheHost()
    {
        // The per-event guard cannot help when the stream itself dies: the outer catch is all that
        // stands between a broken bus and an unobserved task exception taking the process down.
        var logger = new RecordingLogger<VendingHostedService>();
        var h = Harness.Create(bus: new StubBus(faulted: true), logger: logger);
        await using var _ = h;

        await h.Service.StartAsync(CancellationToken.None);

        await WaitForAsync(() => logger.ErrorCount == 3, "not every loop reported its own fault");
        await h.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ASubscriptionThatSimplyEnds_IsNotReportedAsAFault()
    {
        // A stream that completes is the bus saying "no more events", not a failure. Logging it as one
        // would fill the error log with noise on every ordinary shutdown.
        var logger = new RecordingLogger<VendingHostedService>();
        var h = Harness.Create(bus: new StubBus(faulted: false), logger: logger);
        await using var _ = h;

        await h.Service.StartAsync(CancellationToken.None);

        // StopAsync joins the loop tasks, so by the time it returns all three have run to completion.
        await h.Service.StopAsync(CancellationToken.None);

        Assert.Equal(0, logger.ErrorCount);
    }

    /// <summary>The hosted service under test wired to real relay and purger over substituted edges.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private Harness(
            VendingHostedService service,
            IEventBus bus,
            VendingIndex index,
            IVendingStore store,
            IVendingChannelLocator locator,
            ServiceProvider provider)
        {
            Service = service;
            Bus = bus;
            Index = index;
            Store = store;
            Locator = locator;
            _provider = provider;
        }

        public VendingHostedService Service { get; }

        public IEventBus Bus { get; }

        public VendingIndex Index { get; }

        public IVendingStore Store { get; }

        public IVendingChannelLocator Locator { get; }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Service.Dispose();
            await _provider.DisposeAsync().ConfigureAwait(false);
        }

        public static Harness Create(IEventBus? bus = null, ILogger<VendingHostedService>? logger = null)
        {
            var (provider, store, _) = VendingScopeFixture.Create();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var locator = Substitute.For<IVendingChannelLocator>();
            locator.GetChannelIdAsync(default, Guid.Empty, default).ReturnsForAnyArgs((ulong?)null);
            var poster = Substitute.For<IVendingChannelPoster>();
            var index = new VendingIndex();

            var relay = new VendingNotificationRelay(
                index,
                scopeFactory,
                locator,
                poster,
                new VendingEmbedRenderer(Substitute.For<IItemDatabase>(), Substitute.For<ILocalizer>()),
                Options.Create(new VendingOptions()),
                NullLogger<VendingNotificationRelay>.Instance);
            var purger = new VendingWipePurger(
                scopeFactory, index, locator, poster, NullLogger<VendingWipePurger>.Instance);

            var eventBus = bus ?? new InMemoryEventBus();
            var service = new VendingHostedService(
                eventBus, relay, purger, logger ?? NullLogger<VendingHostedService>.Instance);
            return new Harness(service, eventBus, index, store, locator, provider);
        }
    }

    /// <summary>A bus whose subscriptions either fault on first read or end without yielding.</summary>
    /// <param name="faulted">True to throw on the first read; false to end the stream immediately.</param>
    private sealed class StubBus(bool faulted) : IEventBus
    {
        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : notnull => ValueTask.CompletedTask;

        public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : notnull => new StubStream<TEvent>(faulted);
    }

    /// <summary>A stream that yields nothing, either by throwing on the first read or by ending.</summary>
    /// <typeparam name="T">The element type that would have been yielded.</typeparam>
    /// <param name="faulted">True to throw on the first read; false to report the end of the stream.</param>
    private sealed class StubStream<T>(bool faulted) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public T Current => default!;

        public ValueTask<bool> MoveNextAsync() => faulted
            ? ValueTask.FromException<bool>(new InvalidOperationException("subscription faulted"))
            : ValueTask.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Counts the error-level records the three loops emit; written from three threads.</summary>
    /// <typeparam name="T">The logger category.</typeparam>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private int _errors;

        public int ErrorCount => Volatile.Read(ref _errors);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Interlocked.Increment(ref _errors);
            }
        }
    }
}
