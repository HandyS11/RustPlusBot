using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests.Hosting;

public sealed class ChatHostedServiceTests
{
    private static (ChatHostedService Service, InMemoryEventBus Bus, ITeamChatWebhookPoster Poster,
        ITeamChatChannelLocator Locator)
        Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);

        var poster = Substitute.For<ITeamChatWebhookPoster>();
        var locator = Substitute.For<ITeamChatChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)555UL);
        // The relay reads the scoped IMuteStore command prefix per message; stub a scope that provides it.
        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("!");
        var relayScopeFactory = Substitute.For<IServiceScopeFactory>();
        var relayScope = Substitute.For<IServiceScope>();
        var relayScopeProvider = Substitute.For<IServiceProvider>();
        relayScopeProvider.GetService(typeof(IMuteStore)).Returns(muteStore);
        relayScope.ServiceProvider.Returns(relayScopeProvider);
        relayScopeFactory.CreateScope().Returns(relayScope);
        var relay = new TeamChatRelay(locator, poster, dedup, relayScopeFactory);

        var inboundLocator = Substitute.For<ITeamChatChannelLocator>();
        var sender = Substitute.For<ITeamChatSender>();
        // Processor not exercised by bus-side tests; stub scope factory is sufficient.
        var processor = ChatHostedServiceTestAccess.BuildProcessor(inboundLocator, sender, dedup);

        var bus = new InMemoryEventBus();
        var client = new DiscordSocketClient();
        var service = new ChatHostedService(
            client,
            bus,
            relay,
            processor,
            NullLogger<ChatHostedService>.Instance);

        return (service, bus, poster, locator);
    }

    [Fact]
    public async Task TeamMessageReceivedEvent_routes_to_relay_and_posts_to_discord()
    {
        var (service, bus, poster, _) = Build();
        await service.StartAsync(default);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ITeamChatWebhookPoster.PostAsync)))
        {
            await bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, Guid.NewGuid(), 1UL, "Alice", "hello", FromActivePlayer: false));
            await Task.Delay(20);
        }

        await poster.Received().PostAsync(555UL, "Alice", "hello", Arg.Any<CancellationToken>());

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
        poster.PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulated fault"));

        await service.StartAsync(default);

        // Publish until the relay has actually reached (and thrown from) the poster.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ITeamChatWebhookPoster.PostAsync)))
        {
            await bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, Guid.NewGuid(), 1UL, "Bob", "boom", FromActivePlayer: false));
            await Task.Delay(20);
        }

        // The relay threw, causing the loop to fault and complete (LogRelayLoopFaulted). StopAsync joins the
        // faulted task cleanly — no rethrow. This is crash-isolation, not per-event resilience.
        await poster.Received().PostAsync(Arg.Any<ulong>(), "Bob", "boom", Arg.Any<CancellationToken>());
        await service.StopAsync(default);
    }
}

/// <summary>Provides internal access to build <see cref="TeamChatInboundProcessor"/> for tests.</summary>
internal static class ChatHostedServiceTestAccess
{
    internal static TeamChatInboundProcessor BuildProcessor(
        ITeamChatChannelLocator locator,
        ITeamChatSender sender,
        RelayDedupBuffer dedup)
    {
        // A NullScopeFactory is sufficient — processor is not exercised by the bus relay path under test.
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        return new TeamChatInboundProcessor(locator, sender, dedup, scopeFactory);
    }
}
