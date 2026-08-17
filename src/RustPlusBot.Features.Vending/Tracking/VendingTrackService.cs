using System.Globalization;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Tracking;

/// <summary>
/// Registers and lists the grid cells and hand-registered listings a team tracks. Scoped: it depends on
/// the scoped <see cref="IVendingStore"/>, while the world state it validates against comes from the
/// singleton <see cref="VendingIndex"/>.
/// </summary>
/// <param name="store">Persists grid registrations and hand-registered listings.</param>
/// <param name="index">The live per-server vending state, for grid validation and cell counts.</param>
/// <param name="mapSettings">Resolves the grid convention a guild/server displays.</param>
internal sealed class VendingTrackService(
    IVendingStore store,
    VendingIndex index,
    IMapSettingsStore mapSettings) : IVendingTrackService
{
    /// <inheritdoc />
    public async Task<GridTrackResult> TrackGridAsync(
        ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var normalized = grid.Trim().ToUpperInvariant();

        // Validate against the live map rather than a regex: "Z99" is well-formed but does not exist on a
        // 4000 world, and silently registering a cell nobody can reach is worse than an error.
        if (!index.TryGet(guildId, serverId, out var state) || state.WorldSize == 0)
        {
            return new GridTrackResult(GridValid: false, 0, 0);
        }

        var settings = await mapSettings.GetAsync(guildId, serverId, ct).ConfigureAwait(false);
        var cells = MapGrid.CellCount(state.WorldSize);
        var valid = Enumerable.Range(0, cells)
            .SelectMany(c => Enumerable.Range(0, cells)
                .Select(r => MapGrid.ColumnLetters(c) + r.ToString(CultureInfo.InvariantCulture)))
            .Contains(normalized, StringComparer.OrdinalIgnoreCase);
        if (!valid)
        {
            return new GridTrackResult(GridValid: false, 0, 0);
        }

        await store.AddGridAsync(guildId, serverId, normalized, steamId, ct).ConfigureAwait(false);

        List<VendingMachineSnapshot> inCell = [.. state.Machines
            .Where(m => string.Equals(
                GridOwnership.GridOf(m, state.WorldSize, settings.GridStyle),
                normalized,
                StringComparison.OrdinalIgnoreCase))];
        var listings = inCell
            .SelectMany(m => GridOwnership.ToOffers(m, state.WorldSize, settings.GridStyle))
            .Select(o => o.Key)
            .Distinct()
            .Count();

        return new GridTrackResult(GridValid: true, inCell.Count, listings);
    }

    /// <inheritdoc />
    public Task<bool> UntrackGridAsync(ulong guildId, Guid serverId, string grid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return store.RemoveGridAsync(guildId, serverId, grid.Trim().ToUpperInvariant(), ct);
    }

    /// <inheritdoc />
    public Task TrackListingAsync(
        ulong guildId,
        Guid serverId,
        ListingKey key,
        int quantity,
        int costPerOrder,
        ulong userId,
        CancellationToken ct) =>
        store.UpsertListingAsync(guildId, serverId, key, quantity, costPerOrder, userId, ct);

    /// <inheritdoc />
    public Task<bool> UntrackListingAsync(ulong guildId, Guid serverId, ListingKey key, CancellationToken ct) =>
        store.RemoveListingAsync(guildId, serverId, key, ct);

    /// <inheritdoc />
    public async Task<VendingTrackSummary> GetTrackedAsync(ulong guildId, Guid serverId, CancellationToken ct)
    {
        var grids = await store.ListGridsAsync(guildId, serverId, ct).ConfigureAwait(false);
        var listings = await store.ListListingsAsync(guildId, serverId, ct).ConfigureAwait(false);
        return new VendingTrackSummary(grids, [.. listings.Select(ToTrackedListing)]);
    }

    private static TrackedListing ToTrackedListing(VendingListingTrack listing) => new(
        new ListingKey(listing.ItemId, listing.ItemIsBlueprint, listing.CurrencyId, listing.CurrencyIsBlueprint),
        listing.Quantity,
        listing.CostPerOrder);
}
