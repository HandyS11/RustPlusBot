using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Vending;

/// <summary>EF-backed <see cref="IVendingStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies write timestamps.</param>
internal sealed class VendingStore(BotDbContext context, IClock clock) : IVendingStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListGridsAsync(
        ulong guildId, Guid serverId, CancellationToken ct = default) =>
        await context.VendingGridTracks
            .AsNoTracking()
            .Where(g => g.GuildId == guildId && g.ServerId == serverId)
            .OrderBy(g => g.Grid)
            .Select(g => g.Grid)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddGridAsync(ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct = default)
    {
        var normalized = Normalize(grid);
        var existing = await context.VendingGridTracks
            .FirstOrDefaultAsync(g => g.GuildId == guildId && g.ServerId == serverId && g.Grid == normalized, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return; // Re-registering a cell is a no-op, not an error: !vtrack is a natural thing to repeat.
        }

        context.VendingGridTracks.Add(new VendingGridTrack
        {
            GuildId = guildId,
            ServerId = serverId,
            Grid = normalized,
            RegisteredBySteamId = steamId,
            CreatedUtc = clock.UtcNow,
        });
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveGridAsync(ulong guildId, Guid serverId, string grid, CancellationToken ct = default)
    {
        var normalized = Normalize(grid);
        var existing = await context.VendingGridTracks
            .FirstOrDefaultAsync(g => g.GuildId == guildId && g.ServerId == serverId && g.Grid == normalized, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        context.VendingGridTracks.Remove(existing);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task PurgeGridsAsync(ulong guildId, Guid serverId, CancellationToken ct = default)
    {
        await context.VendingGridTracks
            .Where(g => g.GuildId == guildId && g.ServerId == serverId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendingListingTrack>> ListListingsAsync(
        ulong guildId, Guid serverId, CancellationToken ct = default) =>
        await context.VendingListingTracks
            .AsNoTracking()
            .Where(l => l.GuildId == guildId && l.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task UpsertListingAsync(
        ulong guildId, Guid serverId, ListingKey key, int quantity, int costPerOrder, ulong userId,
        CancellationToken ct = default)
    {
        var row = await context.VendingListingTracks
            .FirstOrDefaultAsync(
                l => l.GuildId == guildId && l.ServerId == serverId
                     && l.ItemId == key.ItemId && l.ItemIsBlueprint == key.ItemIsBlueprint
                     && l.CurrencyId == key.CurrencyId && l.CurrencyIsBlueprint == key.CurrencyIsBlueprint,
                ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new VendingListingTrack
            {
                GuildId = guildId,
                ServerId = serverId,
                ItemId = key.ItemId,
                ItemIsBlueprint = key.ItemIsBlueprint,
                CurrencyId = key.CurrencyId,
                CurrencyIsBlueprint = key.CurrencyIsBlueprint,
                RegisteredByUserId = userId,
                CreatedUtc = clock.UtcNow,
            };
            context.VendingListingTracks.Add(row);
        }

        row.Quantity = Math.Max(1, quantity);
        row.CostPerOrder = costPerOrder;
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveListingAsync(
        ulong guildId, Guid serverId, ListingKey key, CancellationToken ct = default)
    {
        var row = await context.VendingListingTracks
            .FirstOrDefaultAsync(
                l => l.GuildId == guildId && l.ServerId == serverId
                     && l.ItemId == key.ItemId && l.ItemIsBlueprint == key.ItemIsBlueprint
                     && l.CurrencyId == key.CurrencyId && l.CurrencyIsBlueprint == key.CurrencyIsBlueprint,
                ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        context.VendingListingTracks.Remove(row);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendingNotification>> ListNotificationsAsync(
        ulong guildId, Guid serverId, CancellationToken ct = default) =>
        await context.VendingNotifications
            .AsNoTracking()
            .Where(n => n.GuildId == guildId && n.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task UpsertNotificationAsync(
        ulong guildId, Guid serverId, ListingKey key, ulong messageId, int referenceQuantity,
        int referenceCostPerOrder, CancellationToken ct = default)
    {
        var row = await context.VendingNotifications
            .FirstOrDefaultAsync(
                n => n.GuildId == guildId && n.ServerId == serverId
                     && n.ItemId == key.ItemId && n.ItemIsBlueprint == key.ItemIsBlueprint
                     && n.CurrencyId == key.CurrencyId && n.CurrencyIsBlueprint == key.CurrencyIsBlueprint,
                ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new VendingNotification
            {
                GuildId = guildId,
                ServerId = serverId,
                ItemId = key.ItemId,
                ItemIsBlueprint = key.ItemIsBlueprint,
                CurrencyId = key.CurrencyId,
                CurrencyIsBlueprint = key.CurrencyIsBlueprint,
            };
            context.VendingNotifications.Add(row);
        }

        row.MessageId = messageId;
        row.ReferenceQuantity = referenceQuantity;
        row.ReferenceCostPerOrder = referenceCostPerOrder;
        row.PostedUtc = clock.UtcNow;
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveNotificationAsync(
        ulong guildId, Guid serverId, ListingKey key, CancellationToken ct = default)
    {
        var row = await context.VendingNotifications
            .FirstOrDefaultAsync(
                n => n.GuildId == guildId && n.ServerId == serverId
                     && n.ItemId == key.ItemId && n.ItemIsBlueprint == key.ItemIsBlueprint
                     && n.CurrencyId == key.CurrencyId && n.CurrencyIsBlueprint == key.CurrencyIsBlueprint,
                ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        context.VendingNotifications.Remove(row);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendingStockNotification>> ListStockNotificationsAsync(
        ulong guildId, Guid serverId, CancellationToken ct = default) =>
        await context.VendingStockNotifications
            .AsNoTracking()
            .Where(s => s.GuildId == guildId && s.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task UpsertStockNotificationAsync(
        ulong guildId, Guid serverId, ulong machineId, ulong messageId, string soldOutSignature,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(soldOutSignature);

        var row = await context.VendingStockNotifications
            .FirstOrDefaultAsync(
                s => s.GuildId == guildId && s.ServerId == serverId && s.MachineId == machineId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new VendingStockNotification
            {
                GuildId = guildId,
                ServerId = serverId,
                MachineId = machineId,
            };
            context.VendingStockNotifications.Add(row);
        }

        row.MessageId = messageId;
        row.SoldOutSignature = soldOutSignature;
        row.PostedUtc = clock.UtcNow;
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveStockNotificationAsync(
        ulong guildId, Guid serverId, ulong machineId, CancellationToken ct = default)
    {
        var row = await context.VendingStockNotifications
            .FirstOrDefaultAsync(
                s => s.GuildId == guildId && s.ServerId == serverId && s.MachineId == machineId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        context.VendingStockNotifications.Remove(row);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static string Normalize(string grid) => grid.Trim().ToUpperInvariant();
}
