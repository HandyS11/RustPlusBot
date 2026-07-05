using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Tests;

/// <summary>Unit tests for <see cref="AlarmStateRelay"/>.</summary>
public sealed class AlarmStateRelayTests
{
    private static readonly DateTimeOffset _fixedNow = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Harness Create(SmartAlarm? alarm = null, ulong? channelId = 777UL)
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

        var refresher = Substitute.For<IAlarmRefresher>();
        var locator = Substitute.For<IAlarmChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(channelId);

        var poster = Substitute.For<IAlarmChannelPoster>();
        var teamChatSender = Substitute.For<ITeamChatSender>();
        teamChatSender
            .SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TeamChatSendResult.Sent);

        var alarmLocalizer = new ResxLocalizer();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_fixedNow);

        if (alarm is not null)
        {
            store.GetAsync(alarm.GuildId, alarm.ServerId, alarm.EntityId, Arg.Any<CancellationToken>())
                .Returns(alarm);
        }

        var relay = new AlarmStateRelay(
            scopeFactory,
            refresher,
            new AlarmRelayChannels(locator, poster, teamChatSender),
            alarmLocalizer,
            clock,
            NullLogger<AlarmStateRelay>.Instance);

        return new Harness(relay, store, refresher, poster, teamChatSender, connections);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Observed state (prime/sweep/refresh) — silent sync
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>An observed state that drifted persists (without a trigger timestamp) and refreshes — but never notifies.</summary>
    [Fact]
    public async Task Observed_drifted_state_persists_and_refreshes_without_notifying()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Perimeter",
            LastIsActive = false,
            PingEveryone = true,
            RelayToTeamChat = true,
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleStateObservedAsync(
            new SmartDeviceStateObservedEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Store.Received(1).UpdateStateAsync(
            10UL, serverId, 42UL, true, null, Arg.Any<CancellationToken>());
        await h.Refresher.Received(1)
            .RefreshAsync(10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive()
            .SendEveryonePingAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.TeamChatSender.DidNotReceive()
            .SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An observed state equal to the persisted one is a no-op (no store write, no Discord edit).</summary>
    [Fact]
    public async Task Observed_unchanged_state_does_nothing()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Perimeter",
            LastIsActive = true,
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleStateObservedAsync(
            new SmartDeviceStateObservedEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Store.DidNotReceive().UpdateStateAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await h.Refresher.DidNotReceive().RefreshAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An observed state for an entity that is not a managed alarm is ignored.</summary>
    [Fact]
    public async Task Observed_foreign_entity_is_ignored()
    {
        var h = Create(alarm: null);

        await h.Relay.HandleStateObservedAsync(
            new SmartDeviceStateObservedEvent(10UL, Guid.NewGuid(), 99UL, IsActive: true), CancellationToken.None);

        await h.Store.DidNotReceive().UpdateStateAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await h.Refresher.DidNotReceive().RefreshAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Triggered → active
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Active trigger on a managed alarm persists state (with timestamp) and refreshes the embed.</summary>
    [Fact]
    public async Task Triggered_active_managed_alarm_updates_state_and_refreshes()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Perimeter",
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Store.Received(1).UpdateStateAsync(
            10UL, serverId, 42UL, true, _fixedNow, Arg.Any<CancellationToken>());
        await h.Refresher.Received(1)
            .RefreshAsync(10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());
    }

    /// <summary>Active trigger with PingEveryone sends the @everyone message.</summary>
    [Fact]
    public async Task Triggered_active_ping_everyone_set_sends_ping()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Fire",
            PingEveryone = true,
        };
        var h = Create(alarm: alarm, channelId: 777UL);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Poster.Received(1).SendEveryonePingAsync(777UL, "@everyone Fire", Arg.Any<CancellationToken>());
    }

    /// <summary>Active trigger without PingEveryone does NOT send the @everyone message.</summary>
    [Fact]
    public async Task Triggered_active_ping_everyone_unset_does_not_ping()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Fire",
            PingEveryone = false,
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Poster.DidNotReceive()
            .SendEveryonePingAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Active trigger with RelayToTeamChat sends the localized line to team chat.</summary>
    [Fact]
    public async Task Triggered_active_relay_to_team_chat_set_sends_team_chat_line()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Perimeter",
            RelayToTeamChat = true,
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        // "alarm.triggered.teamchat" in EN = "🚨 {0} triggered"
        await h.TeamChatSender.Received(1).SendAsync(
            10UL, serverId, "🚨 Perimeter triggered", Arg.Any<CancellationToken>());
    }

    /// <summary>Active trigger without RelayToTeamChat does NOT send to team chat.</summary>
    [Fact]
    public async Task Triggered_active_relay_to_team_chat_unset_does_not_send_team_chat()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Perimeter",
            RelayToTeamChat = false,
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.TeamChatSender.DidNotReceive()
            .SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Triggered → inactive
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Inactive trigger persists state (null timestamp), refreshes embed, no ping/relay.</summary>
    [Fact]
    public async Task Triggered_inactive_updates_state_and_refreshes_no_ping_no_relay()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Perimeter",
            PingEveryone = true,
            RelayToTeamChat = true,
        };
        var h = Create(alarm: alarm);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: false), CancellationToken.None);

        await h.Store.Received(1).UpdateStateAsync(
            10UL, serverId, 42UL, false, null, Arg.Any<CancellationToken>());
        await h.Refresher.Received(1)
            .RefreshAsync(10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive()
            .SendEveryonePingAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.TeamChatSender.DidNotReceive()
            .SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Triggered → non-alarm id
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Entity id not in alarm store is ignored — no update, no refresh, no ping, no relay.</summary>
    [Fact]
    public async Task Triggered_non_alarm_id_is_ignored()
    {
        var serverId = Guid.NewGuid();
        // store returns null by default (not configured for this identity)
        var h = Create(alarm: null);

        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 99UL, IsActive: true), CancellationToken.None);

        await h.Store.DidNotReceive().UpdateStateAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await h.Refresher.DidNotReceive().RefreshAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive()
            .SendEveryonePingAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.TeamChatSender.DidNotReceive()
            .SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Relay SendAsync throws → swallowed
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>If team-chat SendAsync throws, the exception is swallowed and the update+refresh+ping still complete.</summary>
    [Fact]
    public async Task Triggered_relay_send_throws_is_swallowed_and_other_actions_complete()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Fire",
            PingEveryone = true,
            RelayToTeamChat = true,
        };
        var h = Create(alarm: alarm, channelId: 777UL);

        h.TeamChatSender
            .SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("team chat down"));

        // Must not throw
        await h.Relay.HandleTriggeredAsync(
            new SmartDeviceTriggeredEvent(10UL, serverId, 42UL, IsActive: true), CancellationToken.None);

        await h.Store.Received(1).UpdateStateAsync(
            10UL, serverId, 42UL, true, _fixedNow, Arg.Any<CancellationToken>());
        await h.Refresher.Received(1)
            .RefreshAsync(10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).SendEveryonePingAsync(777UL, "@everyone Fire", Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Connection status
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Non-Connected server marks each alarm's embed as unreachable.</summary>
    [Fact]
    public async Task ConnectionStatus_not_connected_refreshes_all_alarms_unreachable()
    {
        var serverId = Guid.NewGuid();
        var h = Create();

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
                new SmartAlarm
                {
                    GuildId = 10UL, ServerId = serverId, EntityId = 43UL, Name = "B"
                },
            ]);

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(10UL, serverId), CancellationToken.None);

        await h.Refresher.Received(1).RefreshAsync(
            Arg.Is<SmartAlarm>(a => a.EntityId == 42UL), unreachable: true, Arg.Any<CancellationToken>());
        await h.Refresher.Received(1).RefreshAsync(
            Arg.Is<SmartAlarm>(a => a.EntityId == 43UL), unreachable: true, Arg.Any<CancellationToken>());
    }

    /// <summary>Connected server → no-op (supervisor's prime path handles it).</summary>
    [Fact]
    public async Task ConnectionStatus_connected_does_nothing()
    {
        var serverId = Guid.NewGuid();
        var h = Create();

        h.Connections.GetStateAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = 10UL, RustServerId = serverId, Status = ConnectionStatus.Connected,
            });

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(10UL, serverId), CancellationToken.None);

        await h.Refresher.DidNotReceive().RefreshAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await h.Refresher.DidNotReceive()
            .RefreshAsync(Arg.Any<SmartAlarm>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reachability changed
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>A reachability change for an owned alarm persists the new value and triggers a refresh.</summary>
    [Fact]
    public async Task ReachabilityChanged_owned_alarm_persists_and_refreshes()
    {
        var serverId = Guid.NewGuid();
        var h = Create();

        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        await h.Relay.HandleReachabilityChangedAsync(
            new DeviceReachabilityChangedEvent(10UL, serverId, 42UL, DeviceReachability.NoPrivilege),
            CancellationToken.None);

        await h.Store.Received(1)
            .SetReachabilityAsync(10UL, serverId, 42UL, DeviceReachability.NoPrivilege,
                Arg.Any<CancellationToken>());
        await h.Refresher.Received(1)
            .RefreshAsync(10UL, serverId, 42UL, unreachable: false, Arg.Any<CancellationToken>());
    }

    /// <summary>A reachability change for a foreign entity (not in the alarm store) is silently ignored.</summary>
    [Fact]
    public async Task ReachabilityChanged_foreign_entity_is_ignored()
    {
        var serverId = Guid.NewGuid();
        var h = Create();

        // ExistsAsync returns false by default for unknown entities.
        h.Store.ExistsAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await h.Relay.HandleReachabilityChangedAsync(
            new DeviceReachabilityChangedEvent(10UL, serverId, 99UL, DeviceReachability.Removed),
            CancellationToken.None);

        await h.Store.DidNotReceive()
            .SetReachabilityAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
                Arg.Any<DeviceReachability>(), Arg.Any<CancellationToken>());
        await h.Refresher.DidNotReceive()
            .RefreshAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        AlarmStateRelay Relay,
        IAlarmStore Store,
        IAlarmRefresher Refresher,
        IAlarmChannelPoster Poster,
        ITeamChatSender TeamChatSender,
        IConnectionStore Connections);
}
