using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.StorageMonitors;

namespace RustPlusBot.Persistence.StorageMonitors;

/// <summary>EF-backed <see cref="IStorageMonitorStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the creation timestamp.</param>
public sealed class StorageMonitorStore(BotDbContext context, IClock clock) : IStorageMonitorStore
{
    /// <inheritdoc />
    public async Task<SmartStorageMonitor> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = new SmartStorageMonitor
        {
            GuildId = guildId,
            ServerId = serverId,
            EntityId = entityId,
            Name = name,
            PairedByUserId = pairedByUserId,
            CreatedUtc = clock.UtcNow,
        };
        context.SmartStorageMonitors.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return entity;
        }
        catch (DbUpdateException)
        {
            // Two users accepted the same pending pairing concurrently (both saw ExistsAsync == false); the
            // unique (GuildId, ServerId, EntityId) index rejects the second insert. Recover idempotently by
            // detaching the failed insert and returning the row the winner persisted. If no such row exists,
            // the failure was not the uniqueness race — let it propagate.
            context.Entry(entity).State = EntityState.Detached;
            var existing = await GetAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    /// <inheritdoc />
    public Task<SmartStorageMonitor?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        context.SmartStorageMonitors.SingleOrDefaultAsync(
            s => s.GuildId == guildId && s.ServerId == serverId && s.EntityId == entityId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SmartStorageMonitor>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        // SQLite cannot ORDER BY a DateTimeOffset column, so order oldest-first on the client side.
        var monitors = await context.SmartStorageMonitors
            .Where(s => s.GuildId == guildId && s.ServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. monitors.OrderBy(s => s.CreatedUtc)];
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        context.SmartStorageMonitors.AnyAsync(
            s => s.GuildId == guildId && s.ServerId == serverId && s.EntityId == entityId, cancellationToken);

    /// <inheritdoc />
    public Task RenameAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        CancellationToken cancellationToken = default) =>
        MutateAsync(guildId, serverId, entityId, s => s.Name = name, cancellationToken);

    /// <inheritdoc />
    public Task SetMessageIdAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong messageId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(guildId, serverId, entityId, s => s.MessageId = messageId, cancellationToken);

    /// <inheritdoc />
    public Task SetReachabilityAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        DeviceReachability reachability,
        CancellationToken cancellationToken = default) =>
        MutateAsync(guildId, serverId, entityId, s => s.Reachability = reachability, cancellationToken);

    /// <inheritdoc />
    public async Task RemoveAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        context.SmartStorageMonitors.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MutateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        Action<SmartStorageMonitor> mutate,
        CancellationToken cancellationToken)
    {
        var entity = await GetAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        mutate(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
