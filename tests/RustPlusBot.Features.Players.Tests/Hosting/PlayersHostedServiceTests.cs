using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Hosting;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Tests.Hosting;

public sealed class PlayersHostedServiceTests
{
    private static Harness Create()
    {
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");
        var services = new ServiceCollection();
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IEventChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);
        var poster = Substitute.For<IPlayerChannelPoster>();
        var sender = Substitute.For<IBotTeamChatSender>();
        sender.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TeamChatSendResult.Sent);

        var relay = new PlayerEventRelay(
            new PlayerEventRenderer(new ResxLocalizer()),
            locator,
            poster,
            sender,
            scopeFactory);

        var bus = new InMemoryEventBus();
        var service = new PlayersHostedService(bus, relay, NullLogger<PlayersHostedService>.Instance);

        return new Harness(service, bus, sender, locator, poster);
    }

    [Fact]
    public async Task PlayerStateChangedEvent_routes_to_relay_and_sends_ingame_line()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Sender.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IBotTeamChatSender.SendAsync)))
        {
            await h.Bus.PublishAsync(new PlayerStateChangedEvent(
                10UL, serverId,
                new MapDimensions(3000, 3000, 0, WorldSize: 3000),
                [new PlayerTransition(PlayerTransitionKind.Connect, 1UL, "Alice", null)]));
            await Task.Delay(20);
        }

        await h.Sender.Received().SendAsync(
            10UL, serverId, Arg.Is<string>(s => s.Contains("Alice")), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task StartAsync_then_StopAsync_completes_cleanly()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var stop = h.Service.StopAsync(default);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RelayLoop_faults_on_sender_exception_but_StopAsync_completes_cleanly()
    {
        var h = Create();
        h.Sender.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulated fault"));

        await h.Service.StartAsync(default);

        // Publish until the relay has actually reached (and thrown from) the in-game sender.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Sender.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IBotTeamChatSender.SendAsync)))
        {
            await h.Bus.PublishAsync(new PlayerStateChangedEvent(
                10UL, Guid.NewGuid(),
                new MapDimensions(3000, 3000, 0, WorldSize: 3000),
                [new PlayerTransition(PlayerTransitionKind.Connect, 1UL, "Bob", null)]));
            await Task.Delay(20);
        }

        // The relay threw, causing the loop to fault and complete (LogRelayLoopFaulted). StopAsync joins the
        // faulted task cleanly — no rethrow. This is crash-isolation, not per-event resilience.
        await h.Sender.Received().SendAsync(
            10UL, Arg.Any<Guid>(), Arg.Is<string>(s => s.Contains("Bob")), Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    private sealed record Harness(
        PlayersHostedService Service,
        InMemoryEventBus Bus,
        IBotTeamChatSender Sender,
        IEventChannelLocator Locator,
        IPlayerChannelPoster Poster);
}
