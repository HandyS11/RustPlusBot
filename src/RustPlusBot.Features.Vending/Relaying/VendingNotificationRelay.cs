using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Features.Vending.Evaluating;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Posting;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Vending;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Vending.Relaying;

/// <summary>
/// Turns each poll into the vending state the world should see: refreshes the search index, then
/// reconciles the desired undercut and sell-out notices against the messages already posted in
/// #vending. One rule governs both kinds: the world's changes edit the message, but the owner's own
/// action (a reprice, a restock) deletes it and reposts, so reacting to an alert produces a new unread
/// instead of a silent edit that still reads as bad news and that nobody notices.
/// </summary>
/// <param name="index">The live per-server vending state, replaced wholesale each poll.</param>
/// <param name="scopeFactory">Opens scopes for the scoped stores (this relay is a singleton).</param>
/// <param name="locator">Resolves the #vending channel id.</param>
/// <param name="poster">Posts, edits and deletes the vending embeds.</param>
/// <param name="renderer">Renders the undercut and sell-out embeds.</param>
/// <param name="options">Per-server notification caps.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class VendingNotificationRelay(
    VendingIndex index,
    IServiceScopeFactory scopeFactory,
    IVendingChannelLocator locator,
    IVendingChannelPoster poster,
    VendingEmbedRenderer renderer,
    IOptions<VendingOptions> options,
    ILogger<VendingNotificationRelay> logger)
{
    /// <summary>Handles one poll: refreshes the index, then reconciles #vending against the new state.</summary>
    /// <param name="evt">The observed vending set for one server.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when every message has been posted, edited or deleted.</returns>
    public async Task HandleObservedAsync(VendingMachinesObservedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // First and unconditionally: search must answer from the latest poll even when nothing is
        // tracked and even when #vending has never been provisioned.
        index.Replace(evt.GuildId, evt.ServerId, evt.WorldSize, evt.Machines);

        if (evt.WorldSize == 0)
        {
            // Grid maths is meaningless without dimensions; acting on it would misfile every machine.
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is not { } cid)
        {
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IVendingStore>();
            var grids = await store.ListGridsAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            var listings = await store.ListListingsAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            var notifications = await store.ListNotificationsAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);
            var stockNotifications = await store.ListStockNotificationsAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);

            if (grids.Count == 0 && listings.Count == 0
                && notifications.Count == 0 && stockNotifications.Count == 0)
            {
                // Nothing is tracked and nothing is posted, so there is nothing to reconcile. Live rows
                // are deliberately part of the test: untracking the last grid leaves messages behind,
                // and it is this pass — with both desired sets empty — that sweeps them away.
                return;
            }

            var settings = await scope.ServiceProvider.GetRequiredService<IMapSettingsStore>()
                .GetAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            var culture = await scope.ServiceProvider.GetRequiredService<IWorkspaceStore>()
                .GetCultureAsync(evt.GuildId, ct).ConfigureAwait(false);

            var owned = new HashSet<string>(grids, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<TrackedListing> tracked = [.. listings.Select(Track)];

            var desired = Cap(
                [.. UndercutEvaluator
                    .Evaluate(evt.Machines, evt.WorldSize, settings.GridStyle, owned, tracked)
                    // The evaluator returns dictionary order, which .NET does not contract. Ordering by
                    // the listing identity keeps the capped set the same one poll after poll.
                    .OrderBy(n => n.Key.ItemId)
                    .ThenBy(n => n.Key.CurrencyId)
                    .ThenBy(n => n.Key.ItemIsBlueprint)
                    .ThenBy(n => n.Key.CurrencyIsBlueprint)],
                evt.ServerId);
            var desiredStock = Cap(
                [.. StockEvaluator
                    .Evaluate(evt.Machines, evt.WorldSize, settings.GridStyle, owned)
                    .OrderBy(n => n.MachineId)],
                evt.ServerId);

            var context = new ReconcileContext(store, cid, evt.GuildId, evt.ServerId, culture);
            await ReconcileUndercutsAsync(context, desired, notifications, ct).ConfigureAwait(false);
            await ReconcileStockAsync(context, desiredStock, stockNotifications, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Handles a connection-status change by dropping the index for a server that went away.</summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task HandleConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _ = ct; // Nothing here awaits; the parameter is the event-bus handler shape.

        if (!evt.IsConnected)
        {
            // Only the index is dropped. A dropped socket says nothing about the world, so deleting the
            // live notifications here would read a lost connection as "every rival vanished".
            index.Clear(evt.GuildId, evt.ServerId);
        }

        return Task.CompletedTask;
    }

    /// <summary>Brings the posted undercut messages in line with the notices this poll wants.</summary>
    /// <param name="context">The reconciliation target: store, channel, server and culture.</param>
    /// <param name="desired">The notices that should be live, capped and in a stable order.</param>
    /// <param name="existing">The undercut notifications currently persisted for the server.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when every message has been posted, edited or deleted.</returns>
    private async Task ReconcileUndercutsAsync(
        ReconcileContext context,
        IReadOnlyList<UndercutNotice> desired,
        IReadOnlyList<VendingNotification> existing,
        CancellationToken ct)
    {
        foreach (var notice in desired)
        {
            var row = existing.FirstOrDefault(n => KeyOf(n) == notice.Key);
            if (row is not null && Repriced(row, notice))
            {
                // The owner repriced. Delete rather than edit so reacting to an alert produces a new
                // unread; the repost below re-raises it if rivals still beat the new price.
                await poster.DeleteMessageAsync(context.ChannelId, row.MessageId, ct).ConfigureAwait(false);
                await context.Store
                    .RemoveNotificationAsync(context.GuildId, context.ServerId, notice.Key, ct)
                    .ConfigureAwait(false);
                row = null;
            }

            var embed = renderer.RenderUndercut(notice, context.Culture);
            var messageId = await poster.EnsureAsync(context.ChannelId, row?.MessageId, embed, ct)
                .ConfigureAwait(false);

            // EnsureAsync returns the same id when the render gate suppressed the edit, so without this
            // guard a live notice would be re-read and re-written every five seconds for as long as the
            // market condition holds — and its PostedUtc would forever read "just now".
            if (messageId is { } id
                && (row is null
                    || row.MessageId != id
                    || row.ReferenceQuantity != notice.ReferenceQuantity
                    || row.ReferenceCostPerOrder != notice.ReferenceCostPerOrder))
            {
                await context.Store.UpsertNotificationAsync(
                        context.GuildId, context.ServerId, notice.Key, id,
                        notice.ReferenceQuantity, notice.ReferenceCostPerOrder, ct)
                    .ConfigureAwait(false);
            }
        }

        HashSet<ListingKey> wanted = [.. desired.Select(n => n.Key)];
        foreach (var row in existing.Where(n => !wanted.Contains(KeyOf(n))))
        {
            // No rival is at or below our price for this listing any more (or the cap dropped it):
            // the message no longer says anything true, so it goes.
            await poster.DeleteMessageAsync(context.ChannelId, row.MessageId, ct).ConfigureAwait(false);
            await context.Store
                .RemoveNotificationAsync(context.GuildId, context.ServerId, KeyOf(row), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Brings the posted sell-out messages in line with the notices this poll wants.</summary>
    /// <param name="context">The reconciliation target: store, channel, server and culture.</param>
    /// <param name="desired">The notices that should be live, capped and in a stable order.</param>
    /// <param name="existing">The sell-out notifications currently persisted for the server.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when every message has been posted, edited or deleted.</returns>
    private async Task ReconcileStockAsync(
        ReconcileContext context,
        IReadOnlyList<StockNotice> desired,
        IReadOnlyList<VendingStockNotification> existing,
        CancellationToken ct)
    {
        foreach (var notice in desired)
        {
            var row = existing.FirstOrDefault(n => n.MachineId == notice.MachineId);
            if (row is not null && Restocked(row.SoldOutSignature, notice.Signature))
            {
                // Something the message named is back on the shelf, so the owner acted. Delete rather
                // than edit, so the repost below lands as a new unread. A set that only grew is the
                // world moving, not the owner, and falls through to an edit.
                await poster.DeleteMessageAsync(context.ChannelId, row.MessageId, ct).ConfigureAwait(false);
                await context.Store
                    .RemoveStockNotificationAsync(context.GuildId, context.ServerId, notice.MachineId, ct)
                    .ConfigureAwait(false);
                row = null;
            }

            var embed = renderer.RenderStock(notice, context.Culture);
            var messageId = await poster.EnsureAsync(context.ChannelId, row?.MessageId, embed, ct)
                .ConfigureAwait(false);

            // Same guard as the undercut pass: an unchanged live notice must cost no database write.
            if (messageId is { } id
                && (row is null
                    || row.MessageId != id
                    || !string.Equals(row.SoldOutSignature, notice.Signature, StringComparison.Ordinal)))
            {
                await context.Store.UpsertStockNotificationAsync(
                        context.GuildId, context.ServerId, notice.MachineId, id, notice.Signature, ct)
                    .ConfigureAwait(false);
            }
        }

        HashSet<ulong> wanted = [.. desired.Select(n => n.MachineId)];
        foreach (var row in existing.Where(n => !wanted.Contains(n.MachineId)))
        {
            // Everything is back in stock, or the machine is gone from the poll entirely: either way
            // there is nothing left to warn about.
            await poster.DeleteMessageAsync(context.ChannelId, row.MessageId, ct).ConfigureAwait(false);
            await context.Store
                .RemoveStockNotificationAsync(context.GuildId, context.ServerId, row.MachineId, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Trims a desired set to the per-server cap, logging what was dropped.</summary>
    /// <typeparam name="T">The notice type.</typeparam>
    /// <param name="ordered">The desired notices in a stable order, so the same ones survive each poll.</param>
    /// <param name="serverId">The server the notices belong to, for the log line.</param>
    /// <returns>At most <see cref="VendingOptions.MaxNotificationsPerServer"/> notices.</returns>
    private IReadOnlyList<T> Cap<T>(IReadOnlyList<T> ordered, Guid serverId)
    {
        var max = options.Value.MaxNotificationsPerServer;
        if (ordered.Count <= max)
        {
            return ordered;
        }

        LogCapExceeded(logger, ordered.Count - max, serverId);
        return [.. ordered.Take(max)];
    }

    /// <summary>
    /// True when the sold-out set shrank — something the posted message named is back on the shelf, so
    /// the owner acted and the message must be deleted and reposted as a new unread. A set that only
    /// grew is the world moving (another line ran dry) and is left to be edited in place.
    /// <see cref="StockNotice.EmptyMachineSignature"/> is the maximal set: nothing can come back while
    /// it holds, and leaving it for anything else means part of the shop was refilled.
    /// </summary>
    /// <param name="persisted">The signature the live message was rendered against.</param>
    /// <param name="current">The signature this poll produced.</param>
    /// <returns>True when the message must be deleted rather than edited.</returns>
    private static bool Restocked(string persisted, string current)
    {
        if (string.Equals(persisted, current, StringComparison.Ordinal)
            || string.Equals(current, StockNotice.EmptyMachineSignature, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(persisted, StockNotice.EmptyMachineSignature, StringComparison.Ordinal))
        {
            return true;
        }

        HashSet<string> stillSoldOut = [.. SignatureIds(current)];
        return SignatureIds(persisted).Any(id => !stillSoldOut.Contains(id));
    }

    /// <summary>Splits a stored signature back into its item ids; the ids are never interpreted, only compared.</summary>
    /// <param name="signature">A comma-joined signature, never <see cref="StockNotice.EmptyMachineSignature"/>.</param>
    /// <returns>The item ids it names.</returns>
    private static string[] SignatureIds(string signature) =>
        signature.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// True when the owner's own price moved, which is what makes the posted message stale enough to
    /// delete and repost rather than edit. The comparison is by unit price, not by the raw reference
    /// fields: <see cref="UndercutEvaluator"/> picks the reference by unit price and replaces it on an
    /// exact tie, so two owned machines selling "1 for 5" and "10 for 50" yield whichever the poll
    /// happened to visit last. Comparing the fields raw would read that arbitrary flip — and a cosmetic
    /// repackage from "1 for 5" to "2 for 10" — as a reprice, and delete a message for nothing.
    /// </summary>
    /// <param name="row">The persisted notification.</param>
    /// <param name="notice">The notice this poll produced.</param>
    /// <returns>True when the message must be deleted rather than edited.</returns>
    private static bool Repriced(VendingNotification row, UndercutNotice notice) =>
        UnitPrice.Compare(
            row.ReferenceCostPerOrder, row.ReferenceQuantity,
            notice.ReferenceCostPerOrder, notice.ReferenceQuantity) != 0;

    /// <summary>Reads the listing identity off a persisted undercut notification.</summary>
    /// <param name="notification">The persisted row.</param>
    /// <returns>The listing the row is about.</returns>
    private static ListingKey KeyOf(VendingNotification notification) => new(
        notification.ItemId, notification.ItemIsBlueprint,
        notification.CurrencyId, notification.CurrencyIsBlueprint);

    /// <summary>Maps a persisted hand-registered listing onto the evaluator's input shape.</summary>
    /// <param name="listing">The persisted row.</param>
    /// <returns>The tracked listing.</returns>
    private static TrackedListing Track(VendingListingTrack listing) => new(
        new ListingKey(
            listing.ItemId, listing.ItemIsBlueprint, listing.CurrencyId, listing.CurrencyIsBlueprint),
        listing.Quantity,
        listing.CostPerOrder);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Vending notification cap reached for server {ServerId}; dropped {Dropped} notice(s).")]
    private static partial void LogCapExceeded(ILogger logger, int dropped, Guid serverId);

    /// <summary>Everything a reconciliation pass needs beyond the notices themselves.</summary>
    /// <param name="Store">The scoped vending store the pass persists through.</param>
    /// <param name="ChannelId">The #vending channel id.</param>
    /// <param name="GuildId">The owning guild snowflake.</param>
    /// <param name="ServerId">The server being reconciled.</param>
    /// <param name="Culture">The guild culture the embeds are rendered in.</param>
    private sealed record ReconcileContext(
        IVendingStore Store, ulong ChannelId, ulong GuildId, Guid ServerId, string Culture);
}
