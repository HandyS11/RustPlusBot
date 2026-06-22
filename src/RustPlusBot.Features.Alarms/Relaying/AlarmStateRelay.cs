using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Relaying;

/// <summary>
/// Keeps alarm embeds in sync with live socket events: updates state and re-renders on trigger; marks
/// alarms unreachable when the server goes non-Connected.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="refresher">Re-renders a single alarm embed on demand.</param>
/// <param name="locator">Resolves the #alarms channel id.</param>
/// <param name="poster">Posts/edits alarm embeds and sends @everyone pings.</param>
/// <param name="teamChatSender">Relays messages into in-game team chat.</param>
/// <param name="localizer">Resolves localized alarm strings.</param>
/// <param name="clock">Provides the current UTC time.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class AlarmStateRelay(
    IServiceScopeFactory scopeFactory,
    IAlarmRefresher refresher,
    IAlarmChannelLocator locator,
    IAlarmChannelPoster poster,
    ITeamChatSender teamChatSender,
    IAlarmLocalizer localizer,
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
            var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            if (channelId is { } channel)
            {
                await poster.SendEveryonePingAsync(channel, $"@everyone {name}", ct).ConfigureAwait(false);
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
            _ = await teamChatSender.SendAsync(evt.GuildId, evt.ServerId, line, ct).ConfigureAwait(false);
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
