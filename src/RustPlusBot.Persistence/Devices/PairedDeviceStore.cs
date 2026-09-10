using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Domain.Devices;

namespace RustPlusBot.Persistence.Devices;

/// <summary>
/// EF-backed persistence shared by every managed smart-device type: identity lookups scoped to
/// (guild, server, entity), the accepted-pairing insert with its double-accept recovery, and the
/// read-modify-save mutators. Derived stores add only what is specific to their device.
/// </summary>
/// <typeparam name="TEntity">The persisted device row.</typeparam>
/// <param name="context">The bot database context.</param>
public abstract class PairedDeviceStore<TEntity>(BotDbContext context) : IPairedDeviceStore<TEntity>
    where TEntity : PairedDeviceEntity, new()
{
    /// <summary>The device's table.</summary>
    private DbSet<TEntity> Set => context.Set<TEntity>();

    /// <inheritdoc />
    public Task<TEntity> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken = default) =>
        // Adding is idempotent: two users can accept the same pending pairing concurrently, and the
        // unique (GuildId, ServerId, EntityId) index rejects whichever insert lands second. The empty
        // mutation is deliberate — the row the winner wrote is returned untouched, name and all —
        // and Persistord recovers the race by re-reading that winner. CreatedAt is stamped by the
        // TimestampInterceptor.
        Set.UpsertAsync(
            s => s.GuildId == guildId && s.ServerId == serverId && s.EntityId == entityId,
            () => new TEntity
            {
                GuildId = guildId,
                ServerId = serverId,
                EntityId = entityId,
                Name = name,
                PairedByUserId = pairedByUserId,
            },
            _ => { },
            cancellationToken);

    /// <summary>Gets a device by identity, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The device, or null.</returns>
    public Task<TEntity?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        Set.SingleOrDefaultAsync(
            s => s.GuildId == guildId && s.ServerId == serverId && s.EntityId == entityId, cancellationToken);

    /// <summary>Lists every managed device for a server, oldest first.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The managed devices for the server.</returns>
    public async Task<IReadOnlyList<TEntity>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        // SQLite cannot ORDER BY a DateTimeOffset column, so order oldest-first on the client side.
        var devices = await Set
            .Where(s => s.GuildId == guildId && s.ServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. devices.OrderBy(s => s.CreatedAt)];
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        Set.AnyAsync(
            s => s.GuildId == guildId && s.ServerId == serverId && s.EntityId == entityId, cancellationToken);

    /// <summary>Renames a device (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the rename has been persisted.</returns>
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

    /// <summary>Sets a device's reachability (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="reachability">The new reachability value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the reachability has been persisted.</returns>
    public Task SetReachabilityAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        DeviceReachability reachability,
        CancellationToken cancellationToken = default) =>
        MutateAsync(guildId, serverId, entityId, s => s.Reachability = reachability, cancellationToken);

    /// <summary>Removes a device (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the device has been removed.</returns>
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

        Set.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads a device by identity, applies <paramref name="mutate"/> and saves; no-op when absent.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="mutate">The change to apply to the loaded row.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the change has been persisted.</returns>
    protected async Task MutateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        Action<TEntity> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var entity = await GetAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        mutate(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
