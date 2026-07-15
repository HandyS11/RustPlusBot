using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Features.Wipes.Hosting;

namespace RustPlusBot.Features.Wipes.Tests.Hosting;

/// <summary>Verifies <see cref="WipesHostedService"/> routes bus events to the detector and announcer.</summary>
public sealed class WipesHostedServiceTests
{
    private static Harness Create()
    {
        var detector = Substitute.For<IWipeDetector>();
        var announcer = Substitute.For<IWipeAnnouncer>();
        var bus = new InMemoryEventBus();
        var service = new WipesHostedService(bus, detector, announcer,
            NullLogger<WipesHostedService>.Instance);
        return new Harness(service, bus, detector, announcer);
    }

    private static async Task PublishUntilReceivedAsync<TEvent>(
        InMemoryEventBus bus,
        TEvent evt,
        object substitute,
        string methodName)
        where TEvent : notnull
    {
        // Same poll-until-deadline shape as AlarmsHostedServiceTests: the consumer loops attach
        // asynchronously after StartAsync, so re-publish on each tick until the substitute records
        // the call instead of asserting immediately after a single publish.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !substitute.ReceivedCalls().Any(c => c.GetMethodInfo().Name == methodName))
        {
            await bus.PublishAsync(evt);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Connected_transition_runs_the_detector()
    {
        var h = Create();
        await h.Service.StartAsync(default);
        var serverId = Guid.NewGuid();
        var evt = new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: true, WasConnected: false);

        await PublishUntilReceivedAsync(h.Bus, evt, h.Detector, nameof(IWipeDetector.CheckAsync));

        await h.Detector.Received().CheckAsync(10UL, serverId, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task Disconnect_does_not_run_the_detector()
    {
        var h = Create();
        await h.Service.StartAsync(default);
        var evt = new ConnectionStatusChangedEvent(10UL, Guid.NewGuid(), IsConnected: false, WasConnected: true);

        // Re-publish for a window (same eager-subscription concern as PublishUntilReceivedAsync) so the
        // subscriber loop has definitely attached and processed at least one delivery before asserting
        // the negative outcome below.
        for (var i = 0; i < 10; i++)
        {
            await h.Bus.PublishAsync(evt);
            await Task.Delay(20);
        }

        await h.Detector.DidNotReceiveWithAnyArgs().CheckAsync(default, Guid.Empty, default);
        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task Steady_state_republish_does_not_run_the_detector()
    {
        var h = Create();
        await h.Service.StartAsync(default);
        var evt = new ConnectionStatusChangedEvent(10UL, Guid.NewGuid(), IsConnected: true, WasConnected: true);

        // Re-publish for a window (same eager-subscription concern as PublishUntilReceivedAsync) so the
        // subscriber loop has definitely attached and processed at least one delivery before asserting
        // the negative outcome below.
        for (var i = 0; i < 10; i++)
        {
            await h.Bus.PublishAsync(evt);
            await Task.Delay(20);
        }

        await h.Detector.DidNotReceiveWithAnyArgs().CheckAsync(default, Guid.Empty, default);
        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task ServerWipedEvent_routes_to_the_announcer()
    {
        var h = Create();
        await h.Service.StartAsync(default);
        var evt = new ServerWipedEvent(10UL, Guid.NewGuid(), null, null, 1u, 3500u);

        await PublishUntilReceivedAsync(h.Bus, evt, h.Announcer, nameof(IWipeAnnouncer.HandleServerWipedAsync));

        await h.Announcer.Received().HandleServerWipedAsync(evt, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    private sealed record Harness(
        WipesHostedService Service,
        InMemoryEventBus Bus,
        IWipeDetector Detector,
        IWipeAnnouncer Announcer);
}
