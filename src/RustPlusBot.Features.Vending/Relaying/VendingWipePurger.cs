using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Relaying;

/// <summary>
/// Clears a wiped server's vending state: the rows go first (so no stale message can be re-edited),
/// then the Discord messages best-effort. Idempotent — a duplicate wipe event finds nothing to do.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped vending store.</param>
/// <param name="index">The live index, cleared so the first post-wipe poll starts clean.</param>
/// <param name="locator">Resolves the #vending channel id.</param>
/// <param name="poster">Deletes the notification messages.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class VendingWipePurger(
    IServiceScopeFactory scopeFactory,
    VendingIndex index,
    IVendingChannelLocator locator,
    IVendingChannelPoster poster,
    ILogger<VendingWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by clearing the server's vending state.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and messages have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        List<ulong> messageIds;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IVendingStore>();
            var undercuts = await store.ListNotificationsAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);
            var stock = await store.ListStockNotificationsAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);
            messageIds = [.. undercuts.Select(n => n.MessageId), .. stock.Select(n => n.MessageId)];

            foreach (var notification in undercuts)
            {
                await store.RemoveNotificationAsync(
                        evt.GuildId,
                        evt.ServerId,
                        new ListingKey(
                            notification.ItemId,
                            notification.ItemIsBlueprint,
                            notification.CurrencyId,
                            notification.CurrencyIsBlueprint),
                        ct)
                    .ConfigureAwait(false);
            }

            foreach (var notification in stock)
            {
                await store.RemoveStockNotificationAsync(
                        evt.GuildId, evt.ServerId, notification.MachineId, ct)
                    .ConfigureAwait(false);
            }

            // Grid registrations point at a base that no longer exists. Manual listings are just an
            // item and a price the player typed in, and stay exactly as valid next wipe — so they stay.
            await store.PurgeGridsAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        }

        index.Clear(evt.GuildId, evt.ServerId);
        if (messageIds.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var messageId in messageIds)
            {
                await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
            }
        }

        LogPurged(logger, messageIds.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} vending notifications after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
