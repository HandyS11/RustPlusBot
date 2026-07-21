using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests.Hosting;

public sealed class ChatHostedServiceTests
{
    private const ulong TeamChannel = 555UL;
    private const ulong ClanChannel = 666UL;

    private static (ChatHostedService Service, InMemoryEventBus Bus, IChatWebhookPoster Poster, IClanStore ClanStore)
        Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);

        var poster = Substitute.For<IChatWebhookPoster>();

        // Kind is stubbed explicitly on both doubles: NSubstitute returns Team (enum 0) by default, so an
        // unstubbed clan double would silently make the relay treat clan lines as team lines.
        var teamLocator = Substitute.For<IChatChannelLocator>();
        teamLocator.Kind.Returns(ChatChannelKind.Team);
        teamLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)TeamChannel);
        var clanLocator = Substitute.For<IChatChannelLocator>();
        clanLocator.Kind.Returns(ChatChannelKind.Clan);
        clanLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)ClanChannel);

        // The relay reads the scoped IMuteStore command prefix per message; stub a scope that provides it.
        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("!");
        var relayScopeFactory = BuildScopeFactory(muteStore, typeof(IMuteStore));
        var relay = new ChatRelay([teamLocator, clanLocator], poster, dedup, relayScopeFactory);

        var inboundLocator = Substitute.For<IChatChannelLocator>();
        inboundLocator.Kind.Returns(ChatChannelKind.Team);
        var sender = Substitute.For<IChatSender>();
        // Processor not exercised by bus-side tests; stub scope factory is sufficient.
        var processor = ChatHostedServiceTestAccess.BuildProcessor(inboundLocator, sender, dedup);

        // The clan loop records the sender's display name through a scoped IClanStore.
        var clanStore = Substitute.For<IClanStore>();
        var hostScopeFactory = BuildScopeFactory(clanStore, typeof(IClanStore));

        var bus = new InMemoryEventBus();
        var client = new DiscordSocketClient();
        var service = new ChatHostedService(
            client,
            bus,
            relay,
            processor,
            hostScopeFactory,
            NullLogger<ChatHostedService>.Instance);

        return (service, bus, poster, clanStore);
    }

    private static IServiceScopeFactory BuildScopeFactory(object service, Type serviceType)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        scopeProvider.GetService(serviceType).Returns(service);
        scope.ServiceProvider.Returns(scopeProvider);
        scopeFactory.CreateScope().Returns(scope);
        return scopeFactory;
    }

    private static bool Posted(IChatWebhookPoster poster) =>
        poster.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IChatWebhookPoster.PostAsync));

    [Fact]
    public async Task TeamMessageReceivedEvent_routes_to_relay_and_posts_to_discord()
    {
        var (service, bus, poster, _) = Build();
        await service.StartAsync(default);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && !Posted(poster))
        {
            await bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, Guid.NewGuid(), 1UL, "Alice", "hello", FromActivePlayer: false));
            await Task.Delay(20);
        }

        await poster.Received()
            .PostAsync(ChatChannelKind.Team, TeamChannel, "Alice", "hello", Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }

    [Fact]
    public async Task Relays_a_team_message_event()
    {
        var (service, bus, poster, _) = Build();
        await service.StartAsync(default);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && !Posted(poster))
        {
            await bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, Guid.NewGuid(), 1UL, "dave", "hi team", FromActivePlayer: false));
            await Task.Delay(20);
        }

        await poster.Received()
            .PostAsync(ChatChannelKind.Team, TeamChannel, "dave", "hi team", Arg.Any<CancellationToken>());
        await poster.DidNotReceive().PostAsync(ChatChannelKind.Clan, Arg.Any<ulong>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }

    [Fact]
    public async Task Relays_a_clan_message_event()
    {
        var (service, bus, poster, clanStore) = Build();
        var serverId = Guid.NewGuid();
        await service.StartAsync(default);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && !Posted(poster))
        {
            await bus.PublishAsync(
                new ClanMessageReceivedEvent(10UL, serverId, 7UL, "dave", "hi clan", FromActivePlayer: false));
            await Task.Delay(20);
        }

        await poster.Received()
            .PostAsync(ChatChannelKind.Clan, ClanChannel, "dave", "hi clan", Arg.Any<CancellationToken>());
        await poster.DidNotReceive().PostAsync(ChatChannelKind.Team, Arg.Any<ulong>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        // The clan API reports members by Steam id only, so chat is the only place names are learned.
        await clanStore.Received().RecordNameAsync(10UL, serverId, 7UL, "dave", Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }

    [Fact]
    public async Task StartAsync_then_StopAsync_completes_cleanly()
    {
        var (service, _, _, _) = Build();
        await service.StartAsync(default);

        var stop = service.StopAsync(default);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RelayLoop_faults_on_poster_exception_but_StopAsync_completes_cleanly()
    {
        var (service, bus, poster, _) = Build();
        poster.PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulated fault"));

        await service.StartAsync(default);

        // Publish until the relay has actually reached (and thrown from) the poster.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && !Posted(poster))
        {
            await bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, Guid.NewGuid(), 1UL, "Bob", "boom", FromActivePlayer: false));
            await Task.Delay(20);
        }

        // The relay threw, causing the loop to fault and complete (LogTeamRelayLoopFaulted). StopAsync joins the
        // faulted task cleanly — no rethrow. This is crash-isolation, not per-event resilience.
        await poster.Received().PostAsync(ChatChannelKind.Team, Arg.Any<ulong>(), "Bob", "boom",
            Arg.Any<CancellationToken>());
        await service.StopAsync(default);
    }
}

/// <summary>Provides internal access to build <see cref="ChatInboundProcessor"/> for tests.</summary>
internal static class ChatHostedServiceTestAccess
{
    internal static ChatInboundProcessor BuildProcessor(
        IChatChannelLocator locator,
        IChatSender sender,
        RelayDedupBuffer dedup)
    {
        // A stub scope factory is sufficient — the processor is not exercised by the bus relay path under test.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        return new ChatInboundProcessor([locator], sender, dedup, scopeFactory);
    }
}
