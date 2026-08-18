using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Vending;

/// <summary>
/// Persists the vending-tracking feature's state: registered grid cells, hand-registered listings,
/// and the Discord message ids of live undercut and sell-out notifications.
/// </summary>
public interface IVendingStore
{
    /// <summary>Lists the registered grid cells for a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The registered grid labels.</returns>
    Task<IReadOnlyList<string>> ListGridsAsync(ulong guildId, Guid serverId, CancellationToken ct = default);

    /// <summary>Registers a grid cell as belonging to the team, if not already registered.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="grid">The grid reference, e.g. "D7". Normalised before comparison and storage.</param>
    /// <param name="steamId">The Steam id of the player registering the cell.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the registration exists, whether newly created or already present.</returns>
    Task AddGridAsync(ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct = default);

    /// <summary>Removes a registered grid cell.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="grid">The grid reference to remove. Normalised before comparison.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a row actually existed and was removed; false when it was not tracked.</returns>
    Task<bool> RemoveGridAsync(ulong guildId, Guid serverId, string grid, CancellationToken ct = default);

    /// <summary>Removes every registered grid cell for a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when all registrations have been removed.</returns>
    Task PurgeGridsAsync(ulong guildId, Guid serverId, CancellationToken ct = default);

    /// <summary>Lists the hand-registered listings for a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The registered listings.</returns>
    Task<IReadOnlyList<VendingListingTrack>> ListListingsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>Registers or reprices a listing.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="key">The identity of the listing.</param>
    /// <param name="quantity">Items yielded by one order; clamped to at least 1.</param>
    /// <param name="costPerOrder">Currency charged for one order.</param>
    /// <param name="userId">The Discord user registering or updating the listing.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the listing has been persisted.</returns>
    Task UpsertListingAsync(
        ulong guildId,
        Guid serverId,
        ListingKey key,
        int quantity,
        int costPerOrder,
        ulong userId,
        CancellationToken ct = default);

    /// <summary>Removes a hand-registered listing.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="key">The identity of the listing to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a row actually existed and was removed; false when it was not tracked.</returns>
    Task<bool> RemoveListingAsync(ulong guildId, Guid serverId, ListingKey key, CancellationToken ct = default);

    /// <summary>Lists the live undercut notifications for a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The live notifications.</returns>
    Task<IReadOnlyList<VendingNotification>> ListNotificationsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Records or updates the live undercut notification for a listing. A call that would change nothing
    /// writes nothing, and <see cref="VendingNotification.PostedUtc"/> moves only when the message id
    /// does — the relay reconciles every five seconds, so an unconditional write would churn the row
    /// forever and leave the timestamp permanently reading "just now".
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="key">The identity of the listing the notification is about.</param>
    /// <param name="messageId">The Discord message id of the posted notification.</param>
    /// <param name="referenceQuantity">The owner's order quantity the message was rendered against.</param>
    /// <param name="referenceCostPerOrder">The owner's order cost the message was rendered against.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the notification has been persisted.</returns>
    Task UpsertNotificationAsync(
        ulong guildId,
        Guid serverId,
        ListingKey key,
        ulong messageId,
        int referenceQuantity,
        int referenceCostPerOrder,
        CancellationToken ct = default);

    /// <summary>Removes the live undercut notification for a listing.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="key">The identity of the listing the notification is about.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a row actually existed and was removed; false when there was nothing to clear.</returns>
    Task<bool> RemoveNotificationAsync(ulong guildId, Guid serverId, ListingKey key, CancellationToken ct = default);

    /// <summary>Lists the live sell-out notifications for a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The live notifications.</returns>
    Task<IReadOnlyList<VendingStockNotification>> ListStockNotificationsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Records or updates the live sell-out notification for a machine. As with
    /// <see cref="UpsertNotificationAsync"/>, an unchanged call writes nothing and
    /// <see cref="VendingStockNotification.PostedUtc"/> moves only when the message id does.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="machineId">The vending machine's marker id.</param>
    /// <param name="messageId">The Discord message id of the posted notification.</param>
    /// <param name="soldOutSignature">The sold-out set the message was rendered against.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the notification has been persisted.</returns>
    Task UpsertStockNotificationAsync(
        ulong guildId,
        Guid serverId,
        ulong machineId,
        ulong messageId,
        string soldOutSignature,
        CancellationToken ct = default);

    /// <summary>Removes the live sell-out notification for a machine.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="machineId">The vending machine's marker id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a row actually existed and was removed; false when there was nothing to clear.</returns>
    Task<bool> RemoveStockNotificationAsync(
        ulong guildId,
        Guid serverId,
        ulong machineId,
        CancellationToken ct = default);
}
