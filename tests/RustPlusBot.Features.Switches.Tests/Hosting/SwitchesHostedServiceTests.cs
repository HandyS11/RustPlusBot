using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Hosting;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Relaying;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Switches;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Switches.Tests.Hosting;

public sealed class SwitchesHostedServiceTests
{
    private static Harness Create()
    {
        var store = Substitute.For<ISwitchStore>();
        var connections = Substitute.For<IConnectionStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => connections);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        // Relay collaborators
        var relayLocator = Substitute.For<ISwitchChannelLocator>();
        relayLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(777UL);
        var relayPoster = Substitute.For<ISwitchChannelPoster>();
        relayPoster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)900UL);
        var renderer = new SwitchEmbedRenderer(new ResxLocalizer());
        var relay = new SwitchStateRelay(scopeFactory, relayLocator, relayPoster, renderer);

        // Coordinator collaborators
        var pairingLocator = Substitute.For<ISwitchChannelLocator>();
        pairingLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(888UL);
        var pairingPoster = Substitute.For<ISwitchChannelPoster>();
        pairingPoster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)901UL);
        var coordinator = new SwitchPairingCoordinator(scopeFactory, pairingLocator, pairingPoster, renderer);

        var bus = new InMemoryEventBus();
        var service = new SwitchesHostedService(
            bus,
            coordinator,
            relay,
            NullLogger<SwitchesHostedService>.Instance);

        return new Harness(service, bus, store, connections, relayPoster, relayLocator, pairingLocator, pairingPoster);
    }

    [Fact]
    public async Task SwitchPairedEvent_routes_to_coordinator_and_posts_prompt()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.PairingPoster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ISwitchChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new SwitchPairedEvent(10UL, serverId, 42UL));
            await Task.Delay(20);
        }

        await h.PairingPoster.Received().EnsureAsync(
            888UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task SwitchStateChangedEvent_routes_to_relay_and_rerenders()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartSwitch
            {
                GuildId = 10UL,
                ServerId = serverId,
                EntityId = 42UL,
                Name = "G",
                MessageId = 900UL,
            });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ISwitchChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new SwitchStateChangedEvent(10UL, serverId, 42UL, IsActive: true));
            await Task.Delay(20);
        }

        await h.Store.Received().UpdateStateAsync(10UL, serverId, 42UL, true, Arg.Any<CancellationToken>());
        await h.Poster.Received().EnsureAsync(
            777UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task ConnectionStatusChangedEvent_non_connected_routes_to_relay_and_rerenders()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Connections.GetStateAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = 10UL, RustServerId = serverId, Status = ConnectionStatus.Unreachable,
            });
        h.Store.ListByServerAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartSwitch
                {
                    GuildId = 10UL,
                    ServerId = serverId,
                    EntityId = 42UL,
                    Name = "G",
                    MessageId = 900UL,
                },
            ]);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ISwitchChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new ConnectionStatusChangedEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await h.Poster.Received().EnsureAsync(
            777UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task SmartDeviceTriggeredEvent_managed_switch_routes_to_relay_and_rerenders()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartSwitch
            {
                GuildId = 10UL,
                ServerId = serverId,
                EntityId = 42UL,
                Name = "G",
                MessageId = 900UL,
            });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ISwitchChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true));
            await Task.Delay(20);
        }

        await h.Store.Received().UpdateStateAsync(10UL, serverId, 42UL, true, Arg.Any<CancellationToken>());
        await h.Poster.Received().EnsureAsync(
            777UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task DeviceReachabilityChangedEvent_owned_switch_routes_to_relay_and_rerenders()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);
        h.Store.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartSwitch
            {
                GuildId = 10UL,
                ServerId = serverId,
                EntityId = 42UL,
                Name = "Door",
                MessageId = 900UL,
                Reachability = DeviceReachability.Removed,
            });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Poster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ISwitchChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(
                new DeviceReachabilityChangedEvent(10UL, serverId, 42UL, DeviceReachability.Removed));
            await Task.Delay(20);
        }

        await h.Store.Received().SetReachabilityAsync(
            10UL, serverId, 42UL, DeviceReachability.Removed, Arg.Any<CancellationToken>());
        await h.Poster.Received().EnsureAsync(
            777UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task StateLoop_survives_a_faulting_relay_and_StopAsync_completes_cleanly()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.When(s => s.UpdateStateAsync(10UL, serverId, 42UL, Arg.Any<bool>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("relay boom"));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Store.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(ISwitchStore.UpdateStateAsync)))
        {
            await h.Bus.PublishAsync(new SwitchStateChangedEvent(10UL, serverId, 42UL, IsActive: true));
            await Task.Delay(20);
        }

        // The relay threw, causing the loop to fault and complete (LogStateLoopFaulted). StopAsync joins the
        // faulted task cleanly — no rethrow. This is crash-isolation, not per-event resilience.
        await h.Store.Received().UpdateStateAsync(10UL, serverId, 42UL, true, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    private sealed record Harness(
        SwitchesHostedService Service,
        InMemoryEventBus Bus,
        ISwitchStore Store,
        IConnectionStore Connections,
        ISwitchChannelPoster Poster,
        ISwitchChannelLocator Locator,
        ISwitchChannelLocator PairingLocator,
        ISwitchChannelPoster PairingPoster);
}
