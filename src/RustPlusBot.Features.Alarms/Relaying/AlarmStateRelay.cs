using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Relaying;

/// <summary>Bundles the channel/chat collaborators injected into <see cref="AlarmStateRelay"/>.</summary>
/// <param name="Locator">Resolves the #alarms channel id.</param>
/// <param name="Poster">Posts/edits alarm embeds and sends @everyone pings.</param>
/// <param name="TeamChatSender">Relays messages into in-game team chat.</param>
internal sealed record AlarmRelayChannels(
    IAlarmChannelLocator Locator,
    IAlarmChannelPoster Poster,
    IBotTeamChatSender TeamChatSender);

/// <summary>
/// Keeps alarm embeds in sync with live socket events: updates state and re-renders on trigger; marks
/// alarms unreachable when the server goes non-Connected.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="refresher">Re-renders a single alarm embed on demand.</param>
/// <param name="channels">Bundles the channel/chat collaborators.</param>
/// <param name="localizer">Resolves localized alarm strings.</param>
/// <param name="clock">Provides the current UTC time.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class AlarmStateRelay(
    IServiceScopeFactory scopeFactory,
    IAlarmRefresher refresher,
    AlarmRelayChannels channels,
    ILocalizer localizer,
    IClock clock,
    ILogger<AlarmStateRelay> logger)
{
    /// <summary>
    /// Handles a smart-device trigger: if it belongs to a managed alarm, persists the new state,
    /// re-renders its embed, and (on the active edge only) optionally pings @everyone and/or relays
    /// the trigger to in-game team chat.
    /// </summary>
    /// <param name="evt">The device-triggered event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when all relay actions have finished.</returns>
    public async Task HandleTriggeredAsync(SmartDeviceTriggeredEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        bool ping, relay;
        string name;

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var alarm = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, ct).ConfigureAwait(false);
            if (alarm is null)
            {
                return; // not an alarm this relay manages (e.g. a switch) — ignore
            }

            await store.UpdateStateAsync(
                    evt.GuildId,
                    evt.ServerId,
                    evt.EntityId,
                    evt.IsActive,
                    evt.IsActive ? clock.UtcNow : null,
                    ct)
                .ConfigureAwait(false);

            ping = alarm.PingEveryone;
            relay = alarm.RelayToTeamChat;
            name = alarm.Name;
        }

        await refresher.RefreshAsync(evt.GuildId, evt.ServerId, evt.EntityId, unreachable: false, ct)
            .ConfigureAwait(false);

        if (!evt.IsActive)
        {
            return; // only the active edge notifies
        }

        if (ping)
        {
            var channelId = await channels.Locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);
            if (channelId is { } channel)
            {
                await channels.Poster.SendEveryonePingAsync(channel, $"@everyone {name}", ct).ConfigureAwait(false);
            }
        }

        if (relay)
        {
            await RelayToTeamChatSafeAsync(evt, name, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles a connection-status change: if the server is no longer Connected, marks every managed
    /// alarm's embed as unreachable. Connected → no-op (the supervisor's prime republishes real state).
    /// </summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when every affected embed has been re-rendered.</returns>
    public async Task HandleConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connections = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await connections.GetStateAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            if (state is { Status: ConnectionStatus.Connected })
            {
                // The supervisor's prime path republishes real state on connect; nothing to do here.
                return;
            }

            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var alarms = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var alarm in alarms)
            {
                // Reuse the already-loaded alarm rather than re-fetching each by id.
                await refresher.RefreshAsync(alarm, unreachable: true, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Handles an observed (prime/sweep/refresh) state reading: if it drifted from the persisted state,
    /// persists it and re-renders the embed. Never pings or relays — only real triggers notify. The
    /// last-triggered timestamp is left untouched (an observation cannot tell when the change happened).
    /// </summary>
    /// <param name="evt">The observed-state event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when any drift has been persisted and re-rendered.</returns>
    public async Task HandleStateObservedAsync(
        SmartDeviceStateObservedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var alarm = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                .ConfigureAwait(false);
            if (alarm is null || alarm.LastIsActive == evt.IsActive)
            {
                return; // foreign entity, or no drift — nothing to do (no Discord edit in steady state).
            }

            await store.UpdateStateAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.IsActive,
                    triggeredUtc: null, cancellationToken)
                .ConfigureAwait(false);
        }

        await refresher.RefreshAsync(evt.GuildId, evt.ServerId, evt.EntityId, unreachable: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a per-device reachability change: if the entity belongs to a managed alarm, persists the new
    /// reachability and triggers a refresh. Foreign entities are silently ignored.
    /// </summary>
    /// <param name="evt">The device-reachability-changed event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered (or the entity was ignored).</returns>
    public async Task HandleReachabilityChangedAsync(
        DeviceReachabilityChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return; // not an alarm this relay manages — ignore.
            }

            await store.SetReachabilityAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Reachability,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // The refresher re-loads + renders; server-down 'unreachable' stays false here — the connection-status
        // path owns that flag. The renderer reads alarm.Reachability for the per-device reason.
        await refresher.RefreshAsync(evt.GuildId, evt.ServerId, evt.EntityId, unreachable: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RelayToTeamChatSafeAsync(SmartDeviceTriggeredEvent evt, string name, CancellationToken ct)
    {
        try
        {
            string culture;
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                culture = await workspace.GetCultureAsync(evt.GuildId, ct).ConfigureAwait(false);
            }

            var line = localizer.Get("alarm.triggered.teamchat", culture, name);
            _ = await channels.TeamChatSender.SendAsync(evt.GuildId, evt.ServerId, line, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a team-chat relay failure must not block the embed/ping path.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRelayFailed(logger, ex, evt.GuildId, evt.ServerId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Relaying alarm trigger to team chat for guild {GuildId} server {ServerId} failed; swallowing.")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception, ulong guildId, Guid serverId);
}
