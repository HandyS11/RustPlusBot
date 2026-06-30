using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Switches;

namespace RustPlusBot.Persistence.Switches;

/// <summary>EF-backed <see cref="ISwitchStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the creation timestamp.</param>
public sealed class SwitchStore(BotDbContext context, IClock clock) : ISwitchStore
{
    /// <inheritdoc />
    public async Task<SmartSwitch> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = new SmartSwitch
        {
            GuildId = guildId,
            ServerId = serverId,
            EntityId = entityId,
            Name = name,
            PairedByUserId = pairedByUserId,
            LastIsActive = false,
            CreatedUtc = clock.UtcNow,
        };
        context.SmartSwitches.Add(entity);
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
    public Task<SmartSwitch?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        context.SmartSwitches.SingleOrDefaultAsync(
            s => s.GuildId == guildId && s.ServerId == serverId && s.EntityId == entityId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SmartSwitch>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        // SQLite cannot ORDER BY a DateTimeOffset column, so order oldest-first on the client side.
        var switches = await context.SmartSwitches
            .Where(s => s.GuildId == guildId && s.ServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. switches.OrderBy(s => s.CreatedUtc)];
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default) =>
        context.SmartSwitches.AnyAsync(
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
    public Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        MutateAsync(guildId, serverId, entityId, s => s.LastIsActive = isActive, cancellationToken);

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

        context.SmartSwitches.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MutateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        Action<SmartSwitch> mutate,
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
