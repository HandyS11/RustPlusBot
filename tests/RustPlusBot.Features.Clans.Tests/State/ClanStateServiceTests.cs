using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Clans.Messages;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Clans.Posting;
using RustPlusBot.Features.Clans.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Clans.Tests.State;

public sealed class ClanStateServiceTests
{
    private const ulong Guild = 42UL;
    private const ulong Channel = 999UL;
    private static readonly Guid Server = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IClanStore _store = Substitute.For<IClanStore>();
    private readonly IWorkspaceStore _workspace = Substitute.For<IWorkspaceStore>();
    private readonly IClanNameResolver _names = Substitute.For<IClanNameResolver>();
    private readonly IWorkspaceReconciler _reconciler = Substitute.For<IWorkspaceReconciler>();
    private readonly IClanInfoChannelLocator _locator = Substitute.For<IClanInfoChannelLocator>();
    private readonly IClanFeedPoster _poster = Substitute.For<IClanFeedPoster>();
    private readonly ILocalizer _localizer = Substitute.For<ILocalizer>();

    public ClanStateServiceTests()
    {
        _locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((ulong?)Channel);
        _workspace.GetCultureAsync(Guild, Arg.Any<CancellationToken>()).Returns("en");
        _names.ResolveAsync(Guild, Server, Arg.Any<IReadOnlyCollection<ulong>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<ulong, string>());

        // The clan.event.* keys land in Task 12; echo the key so a rendered line is non-null today.
        _localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => (string)ci[0]!);
        _localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>()).Returns(ci => (string)ci[0]!);
    }

    [Fact]
    public async Task Unavailable_changes_nothing()
    {
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.Unavailable, null),
            CancellationToken.None);

        await _store.DidNotReceive().ClearAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().SaveAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ClanSnapshot>(),
            Arg.Any<CancellationToken>());
        await _reconciler.DidNotReceive()
            .ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoClan_clears_the_state_and_reconciles()
    {
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot());
        _store.ClearAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(true);
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.NoClan, null),
            CancellationToken.None);

        await _store.Received(1).ClearAsync(Guild, Server, Arg.Any<CancellationToken>());
        await _reconciler.Received(1).ReconcileServerAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoClan_posts_the_dissolved_line_before_reconciling()
    {
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot());
        _store.ClearAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(true);
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.NoClan, null),
            CancellationToken.None);

        // The reconcile deletes the channel we just posted into, so the order is load-bearing.
#pragma warning disable VSTHRD110 // NSubstitute call specifications: the returned tasks are never awaited.
        Received.InOrder(() =>
        {
            _poster.PostAsync(Channel, "clan.event.dissolved", Arg.Any<CancellationToken>());
            _reconciler.ReconcileServerAsync(Guild, Server, Arg.Any<CancellationToken>());
        });
#pragma warning restore VSTHRD110
    }

    [Fact]
    public async Task NoClan_does_nothing_when_there_was_no_stored_clan()
    {
        _store.ClearAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(false);
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.NoClan, null),
            CancellationToken.None);

        await _reconciler.DidNotReceive()
            .ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task First_detection_saves_and_reconciles_without_posting_changes()
    {
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((ClanSnapshot?)null);
        var snapshot = Snapshot();
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, snapshot),
            CancellationToken.None);

        await _store.Received(1).SaveAsync(Guild, Server, snapshot, Arg.Any<CancellationToken>());
        await _reconciler.Received(1).ReconcileServerAsync(Guild, Server, Arg.Any<CancellationToken>());
        await _poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_subsequent_snapshot_saves_and_posts_changes_without_reconciling()
    {
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot());
        var renamed = Snapshot("Bears");
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, renamed),
            CancellationToken.None);

        await _store.Received(1).SaveAsync(Guild, Server, renamed, Arg.Any<CancellationToken>());
        await _poster.Received(1).PostAsync(Channel, "clan.event.renamed", Arg.Any<CancellationToken>());
        await _reconciler.DidNotReceive()
            .ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_nothing_when_the_snapshot_is_unchanged()
    {
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot());
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, Snapshot()),
            CancellationToken.None);

        await _poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _reconciler.DidNotReceive()
            .ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_post_when_the_claninfo_channel_is_not_provisioned()
    {
        _locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((ulong?)null);
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot());
        var renamed = Snapshot("Bears");
        var service = Build();

        await service.ApplyAsync(new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, renamed),
            CancellationToken.None);

        await _poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.Received(1).SaveAsync(Guild, Server, renamed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Score_changes_are_throttled_to_one_post_per_minute()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var service = Build(clock);

        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot(score: 1));
        await service.ApplyAsync(
            new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, Snapshot(score: 2)),
            CancellationToken.None);

        clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(30);
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot(score: 2));
        await service.ApplyAsync(
            new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, Snapshot(score: 3)),
            CancellationToken.None);

        clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(90);
        _store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(Snapshot(score: 3));
        await service.ApplyAsync(
            new ClanStateChangedEvent(Guild, Server, ClanProbeStatus.HasClan, Snapshot(score: 4)),
            CancellationToken.None);

        // Three score moves, but the one 30s after the first is dropped.
        await _poster.Received(2).PostAsync(Channel, "clan.event.score", Arg.Any<CancellationToken>());
    }

    private static ClanSnapshot Snapshot(string name = "Wolves", long? score = null) =>
        new(7L, name, DateTimeOffset.UnixEpoch, 1UL, null, null, null, null, null, null, score, [], [], []);

    private ClanStateService Build(IClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _store);
        services.AddScoped(_ => _workspace);
        services.AddScoped(_ => _names);
        services.AddScoped(_ => _reconciler);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var effectiveClock = clock ?? new FakeClock(DateTimeOffset.UnixEpoch);

        return new ClanStateService(
            scopeFactory,
            _locator,
            _poster,
            new ClanChangeRenderer(_localizer),
            new ClanCapabilityProvider(scopeFactory, effectiveClock),
            effectiveClock,
            NullLogger<ClanStateService>.Instance);
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
