using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Persistence.Wipes;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Unit tests for <see cref="WipeDetector"/>'s diff rules.</summary>
public sealed class WipeDetectorTests
{
    private static readonly DateTimeOffset OldWipe = new(2026, 6, 4, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid ServerId = Guid.NewGuid();

    private static Harness Create(
        ServerInfoSnapshot? info,
        WorldSnapshot? world,
        WipeBaseline? baseline)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(info);
        query.GetWorldAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(world);

        var store = Substitute.For<IWipeBaselineStore>();
        store.GetAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(baseline);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var bus = Substitute.For<IEventBus>();
        var detector = new WipeDetector(scopeFactory, query, bus, NullLogger<WipeDetector>.Instance);
        return new Harness(detector, query, store, bus);
    }

    private static ServerInfoSnapshot Info(DateTimeOffset? wipeTime) => new(5, 100, 0, wipeTime);

    [Fact]
    public async Task Null_info_snapshot_aborts_silently()
    {
        var h = Create(info: null, world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, Guid.Empty, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Null_world_snapshot_aborts_silently()
    {
        var h = Create(info: Info(OldWipe), world: null, baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Missing_server_row_aborts_silently()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u), baseline: null);

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, Guid.Empty, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Empty_baseline_backfills_without_event()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(null, null, null));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(OldWipe, 42u, 3500u),
            Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Unchanged_baseline_is_a_noop()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, Guid.Empty, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Wipe_time_advance_beyond_tolerance_publishes_event()
    {
        var newWipe = OldWipe.AddDays(7);
        var h = Create(info: Info(newWipe), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(newWipe, 42u, 3500u),
            Arg.Any<CancellationToken>());
        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, newWipe, 42u, 3500u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wipe_time_jitter_within_tolerance_refreshes_baseline_without_event()
    {
        var jittered = OldWipe.AddSeconds(30);
        var h = Create(info: Info(jittered), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(jittered, 42u, 3500u),
            Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Seed_change_publishes_event()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 999u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, OldWipe, 999u, 3500u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Size_change_publishes_event()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(4250u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, OldWipe, 42u, 4250u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Null_observed_wipe_time_preserves_baseline_wipe_time()
    {
        var h = Create(info: Info(null), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, Guid.Empty, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Wipe_time_advance_at_exact_tolerance_is_not_a_wipe()
    {
        var atTolerance = OldWipe.AddSeconds(60);
        var h = Create(info: Info(atTolerance), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(atTolerance, 42u, 3500u),
            Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Wipe_time_advance_just_beyond_tolerance_publishes()
    {
        var beyondTolerance = OldWipe.AddSeconds(61);
        var h = Create(info: Info(beyondTolerance), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, beyondTolerance, 42u, 3500u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wipe_time_appearing_from_null_backfills_without_event()
    {
        // Baseline had seed/size but no wipe time (server started reporting it): not a wipe.
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u),
            baseline: new WipeBaseline(null, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(OldWipe, 42u, 3500u),
            Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    private sealed record Harness(
        WipeDetector Detector,
        IRustServerQuery Query,
        IWipeBaselineStore Store,
        IEventBus Bus);
}
