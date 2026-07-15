using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Features.StorageMonitors.Relaying;

/// <summary>
/// Deletes all of a wiped server's storage monitors: entity ids are permanently invalid after a wipe, so
/// the rows are removed first, then the #storagemonitors embeds best-effort. Idempotent: a duplicate wipe
/// event finds no rows and is a no-op.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped storage-monitor store.</param>
/// <param name="locator">Resolves the #storagemonitors channel id.</param>
/// <param name="poster">Deletes the storage-monitor embeds.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class StorageMonitorWipePurger(
    IServiceScopeFactory scopeFactory,
    IStorageMonitorChannelLocator locator,
    IStorageMonitorChannelPoster poster,
    ILogger<StorageMonitorWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by purging the server's storage monitors.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and embeds have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        IReadOnlyList<SmartStorageMonitor> monitors;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            monitors = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var monitor in monitors)
            {
                await store.RemoveAsync(evt.GuildId, evt.ServerId, monitor.EntityId, ct).ConfigureAwait(false);
            }
        }

        if (monitors.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.MessageId is { } messageId)
                {
                    await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
                }
            }
        }

        LogPurged(logger, monitors.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} storage monitors after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
