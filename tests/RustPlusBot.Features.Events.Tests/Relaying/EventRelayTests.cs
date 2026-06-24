using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Tests.Relaying;

public sealed class EventRelayTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private static (EventRelay Relay, EventStateStore Store, IEventChannelPoster Poster,
        ITeamChatSender Sender, RigStateStore RigStore) CreateRelay(ulong? channelId = 999UL)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero));

        var locator = Substitute.For<IEventChannelLocator>();
        locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(channelId);

        var poster = Substitute.For<IEventChannelPoster>();

        var store = new EventStateStore(clock);

        var workspaceStore = Substitute.For<IWorkspaceStore>();
        workspaceStore.GetCultureAsync(Guild, Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped<IWorkspaceStore>(_ => workspaceStore);
        var provider = services.BuildServiceProvider();

        var sender = Substitute.For<ITeamChatSender>();
        var rigStore = new RigStateStore(clock, Options.Create(new ConnectionOptions()));
        var renderer = new EventEmbedRenderer(new ResxLocalizer());

        var relay = new EventRelay(
            new MarkerEventClassifier(clock),
            store,
            renderer,
            new EventRelayChannels(locator, poster, sender),
            rigStore,
            provider.GetRequiredService<IServiceScopeFactory>());

        return (relay, store, poster, sender, rigStore);
    }

    [Fact]
    public async Task Posts_one_embed_per_classified_event_and_updates_state()
    {
        var (relay, store, poster, _, _) = CreateRelay();

        await relay.RelayAsync(
            new MapMarkersChangedEvent(Guild, Server, null,
                [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)],
                []),
            CancellationToken.None);

        await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        Assert.Single(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
    }

    [Fact]
    public async Task When_channel_missing_updates_state_but_does_not_post()
    {
        var (relay, store, poster, _, _) = CreateRelay(channelId: null);

        await relay.RelayAsync(
            new MapMarkersChangedEvent(Guild, Server, null,
                [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)],
                []),
            CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        // State must still be updated so !cargo reflects the active cargo even without #events.
        Assert.Single(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
    }

    [Fact]
    public async Task Empty_classification_posts_nothing()
    {
        var (relay, _, poster, _, _) = CreateRelay();

        // Other markers produce no domain events from the classifier.
        await relay.RelayAsync(
            new MapMarkersChangedEvent(Guild, Server, null,
                [new MapMarkerSnapshot(2, MarkerKind.Other, 0f, 0f, null)],
                []),
            CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Relay_broadcasts_each_event_in_game_in_addition_to_discord()
    {
        var (relay, _, poster, sender, _) = CreateRelay();

        await relay.RelayAsync(
            new MapMarkersChangedEvent(Guild, Server, null,
                [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)],
                []),
            CancellationToken.None);

        await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(Guild, Server, Arg.Is<string>(s => !string.IsNullOrWhiteSpace(s)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelayRig_activated_applies_state_posts_embed_and_broadcasts()
    {
        var (relay, _, poster, sender, rigStore) = CreateRelay();
        var evt = new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.Activated, 100f, 200f, null);

        await relay.RelayRigAsync(evt, CancellationToken.None);

        Assert.Equal(RigStatus.Active, rigStore.Get(Guild, Server, RigKind.Small).Status);
        await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(Guild, Server, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelayRig_lootable_does_not_apply_but_still_alerts()
    {
        var (relay, _, poster, sender, rigStore) = CreateRelay();
        var evt = new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.CrateLootable, 100f, 200f, null);

        await relay.RelayRigAsync(evt, CancellationToken.None);

        // Not Active — the tick already advanced state; relay must not flip it back to Active.
        Assert.Equal(RigStatus.Online, rigStore.Get(Guild, Server, RigKind.Small).Status);
        await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(Guild, Server, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
