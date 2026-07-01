using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Alarms.Hosting;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Tests.Hosting;

public sealed class AlarmsHostedServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Harness Create()
    {
        var store = Substitute.For<IAlarmStore>();
        var connections = Substitute.For<IConnectionStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => connections);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);

        // Relay collaborators
        var refresher = Substitute.For<IAlarmRefresher>();
        var relayLocator = Substitute.For<IAlarmChannelLocator>();
        relayLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(777UL);
        var relayPoster = Substitute.For<IAlarmChannelPoster>();
        var teamChatSender = Substitute.For<ITeamChatSender>();
        teamChatSender.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TeamChatSendResult.Sent);

        var alarmRenderer = new AlarmEmbedRenderer(new ResxLocalizer(), clock);
        var relay = new AlarmStateRelay(
            scopeFactory,
            refresher,
            new AlarmRelayChannels(relayLocator, relayPoster, teamChatSender),
            new ResxLocalizer(),
            clock,
            NullLogger<AlarmStateRelay>.Instance);

        // Coordinator collaborators
        var pairingLocator = Substitute.For<IAlarmChannelLocator>();
        pairingLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(888UL);
        var pairingPoster = Substitute.For<IAlarmChannelPoster>();
        pairingPoster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>()).Returns((ulong?)901UL);
        var coordinator = new AlarmPairingCoordinator(scopeFactory, pairingLocator, pairingPoster, alarmRenderer);

        var bus = new InMemoryEventBus();
        var service = new AlarmsHostedService(
            bus,
            coordinator,
            relay,
            NullLogger<AlarmsHostedService>.Instance);

        return new Harness(service, bus, store, connections, refresher, relayPoster, pairingLocator, pairingPoster);
    }

    [Fact]
    public async Task AlarmPairedEvent_routes_to_coordinator_and_posts_prompt()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.PairingPoster.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IAlarmChannelPoster.EnsureAsync)))
        {
            await h.Bus.PublishAsync(new AlarmPairedEvent(10UL, serverId, 42UL));
            await Task.Delay(20);
        }

        await h.PairingPoster.Received().EnsureAsync(
            888UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task SmartDeviceTriggeredEvent_managed_alarm_routes_to_relay_and_refreshes()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>())
            .Returns(new SmartAlarm
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Perimeter",
            });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Refresher.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IAlarmRefresher.RefreshAsync)))
        {
            await h.Bus.PublishAsync(new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true));
            await Task.Delay(20);
        }

        await h.Store.Received().UpdateStateAsync(
            10UL, serverId, 42UL, true, FixedNow, Arg.Any<CancellationToken>());
        await h.Refresher.Received().RefreshAsync(
            10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task ConnectionStatusChangedEvent_non_connected_routes_to_relay_and_refreshes_all_alarms()
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
                new SmartAlarm
                {
                    GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "A"
                },
            ]);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Refresher.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IAlarmRefresher.RefreshAsync)))
        {
            await h.Bus.PublishAsync(new ConnectionStatusChangedEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await h.Refresher.Received().RefreshAsync(
            Arg.Is<SmartAlarm>(a => a.EntityId == 42UL), unreachable: true, Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task DeviceReachabilityChangedEvent_owned_alarm_routes_to_relay_and_persists_and_refreshes()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Refresher.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IAlarmRefresher.RefreshAsync)))
        {
            await h.Bus.PublishAsync(
                new DeviceReachabilityChangedEvent(10UL, serverId, 42UL, DeviceReachability.NoPrivilege));
            await Task.Delay(20);
        }

        await h.Store.Received().SetReachabilityAsync(
            10UL, serverId, 42UL, DeviceReachability.NoPrivilege, Arg.Any<CancellationToken>());
        await h.Refresher.Received().RefreshAsync(
            10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task TriggeredLoop_survives_a_faulting_relay_and_StopAsync_completes_cleanly()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        h.Store.When(s => s.GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("relay boom"));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Store.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IAlarmStore.GetAsync)))
        {
            await h.Bus.PublishAsync(new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true));
            await Task.Delay(20);
        }

        // The relay threw; the loop swallowed it (LogTriggeredLoopFaulted) so StopAsync joins cleanly.
        await h.Store.Received().GetAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    private sealed record Harness(
        AlarmsHostedService Service,
        InMemoryEventBus Bus,
        IAlarmStore Store,
        IConnectionStore Connections,
        IAlarmRefresher Refresher,
        IAlarmChannelPoster Poster,
        IAlarmChannelLocator PairingLocator,
        IAlarmChannelPoster PairingPoster);
}
