using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;

namespace RustPlusBot.Persistence.Alarms;

/// <summary>EF-backed <see cref="IAlarmStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the creation timestamp.</param>
public sealed class AlarmStore(BotDbContext context, IClock clock) : IAlarmStore
{
    /// <inheritdoc />
    public async Task<SmartAlarm> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken ct = default)
    {
        var entity = new SmartAlarm
        {
            GuildId = guildId,
            ServerId = serverId,
            EntityId = entityId,
            Name = name,
            PairedByUserId = pairedByUserId,
            CreatedUtc = clock.UtcNow,
        };
        context.SmartAlarms.Add(entity);
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return entity;
        }
        catch (DbUpdateException)
        {
            // Two users accepted the same pending pairing concurrently (both saw ExistsAsync == false); the
            // unique (GuildId, ServerId, EntityId) index rejects the second insert. Recover idempotently by
            // detaching the failed insert and returning the row the winner persisted. If no such row exists,
            // the failure was not the uniqueness race — let it propagate.
            context.Entry(entity).State = EntityState.Detached;
            var existing = await GetAsync(guildId, serverId, entityId, ct).ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    /// <inheritdoc />
    public Task<SmartAlarm?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken ct = default) =>
        context.SmartAlarms.SingleOrDefaultAsync(
            a => a.GuildId == guildId && a.ServerId == serverId && a.EntityId == entityId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SmartAlarm>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken ct = default)
    {
        // SQLite cannot ORDER BY a DateTimeOffset column, so order oldest-first on the client side.
        var alarms = await context.SmartAlarms
            .Where(a => a.GuildId == guildId && a.ServerId == serverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return alarms.OrderBy(a => a.CreatedUtc).ToList();
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken ct = default) =>
        context.SmartAlarms.AnyAsync(
            a => a.GuildId == guildId && a.ServerId == serverId && a.EntityId == entityId, ct);

    /// <inheritdoc />
    public Task RenameAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a => a.Name = name, ct);

    /// <inheritdoc />
    public Task SetMessageIdAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong messageId,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a => a.MessageId = messageId, ct);

    /// <inheritdoc />
    public Task SetPingEveryoneAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a => a.PingEveryone = value, ct);

    /// <inheritdoc />
    public Task SetRelayToTeamChatAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a => a.RelayToTeamChat = value, ct);

    /// <inheritdoc />
    public Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        DateTimeOffset? triggeredUtc,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a =>
        {
            a.LastIsActive = isActive;
            if (triggeredUtc is { } t)
            {
                a.LastTriggeredUtc = t;
            }
        }, ct);

    /// <inheritdoc />
    public async Task RemoveAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken ct = default)
    {
        var entity = await GetAsync(guildId, serverId, entityId, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        context.SmartAlarms.Remove(entity);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task MutateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        Action<SmartAlarm> mutate,
        CancellationToken ct)
    {
        var entity = await GetAsync(guildId, serverId, entityId, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        mutate(entity);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
