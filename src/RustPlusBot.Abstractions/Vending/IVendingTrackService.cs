namespace RustPlusBot.Abstractions.Vending;

/// <summary>Registering and listing the vending listings a team tracks. Scoped.</summary>
public interface IVendingTrackService
{
    /// <summary>Registers a grid cell so every machine inside it counts as the team's own.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="grid">The grid reference, e.g. "D7"; case-insensitive.</param>
    /// <param name="steamId">The Steam id of the registering player.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The outcome, including how many machines and listings the cell holds right now.</returns>
    Task<GridTrackResult> TrackGridAsync(ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct);

    /// <summary>Unregisters a grid cell.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="grid">The grid reference to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a registration was removed; false when the cell was not registered.</returns>
    Task<bool> UntrackGridAsync(ulong guildId, Guid serverId, string grid, CancellationToken ct);

    /// <summary>Registers (or reprices) a listing the team sells, with no machine required.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="key">The listing identity.</param>
    /// <param name="quantity">Items yielded by one order; must be at least 1.</param>
    /// <param name="costPerOrder">Currency charged for one order.</param>
    /// <param name="userId">The Discord user registering it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    Task TrackListingAsync(
        ulong guildId,
        Guid serverId,
        ListingKey key,
        int quantity,
        int costPerOrder,
        ulong userId,
        CancellationToken ct);

    /// <summary>Unregisters a manually tracked listing.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="key">The listing identity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a listing was removed.</returns>
    Task<bool> UntrackListingAsync(ulong guildId, Guid serverId, ListingKey key, CancellationToken ct);

    /// <summary>Lists everything the team currently tracks on a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The registered grid cells and manual listings.</returns>
    Task<VendingTrackSummary> GetTrackedAsync(ulong guildId, Guid serverId, CancellationToken ct);
}

/// <summary>The outcome of registering a grid cell.</summary>
/// <param name="GridValid">False when the reference does not exist on this map, or the world size is unknown.</param>
/// <param name="MachinesFound">Machines currently standing in the cell.</param>
/// <param name="ListingsTracked">Distinct listings those machines currently offer.</param>
public sealed record GridTrackResult(bool GridValid, int MachinesFound, int ListingsTracked);

/// <summary>A manually registered listing.</summary>
/// <param name="Key">The listing identity.</param>
/// <param name="Quantity">Items yielded by one order.</param>
/// <param name="CostPerOrder">Currency charged for one order.</param>
public sealed record TrackedListing(ListingKey Key, int Quantity, int CostPerOrder);

/// <summary>Everything a team tracks on one server.</summary>
/// <param name="Grids">Registered grid references, ascending.</param>
/// <param name="Listings">Manually registered listings.</param>
public sealed record VendingTrackSummary(
    IReadOnlyList<string> Grids,
    IReadOnlyList<TrackedListing> Listings);
