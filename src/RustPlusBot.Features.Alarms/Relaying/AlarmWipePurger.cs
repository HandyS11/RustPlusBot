using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Alarms;

namespace RustPlusBot.Features.Alarms.Relaying;

/// <summary>
/// Deletes all of a wiped server's alarms: entity ids are permanently invalid after a wipe, so the rows
/// are removed first (guaranteeing no stale trigger notifications), then the #alarms embeds best-effort.
/// Idempotent: a duplicate wipe event finds no rows and is a no-op.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped alarm store.</param>
/// <param name="locator">Resolves the #alarms channel id.</param>
/// <param name="poster">Deletes the alarm embeds.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class AlarmWipePurger(
    IServiceScopeFactory scopeFactory,
    IAlarmChannelLocator locator,
    IAlarmChannelPoster poster,
    ILogger<AlarmWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by purging the server's alarms.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and embeds have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        IReadOnlyList<SmartAlarm> alarms;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            alarms = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var alarm in alarms)
            {
                await store.RemoveAsync(evt.GuildId, evt.ServerId, alarm.EntityId, ct).ConfigureAwait(false);
            }
        }

        if (alarms.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var alarm in alarms)
            {
                if (alarm.MessageId is { } messageId)
                {
                    await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
                }
            }
        }

        LogPurged(logger, alarms.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} alarms after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
