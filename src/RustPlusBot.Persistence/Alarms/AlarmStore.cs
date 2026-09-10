using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Alarms;

namespace RustPlusBot.Persistence.Alarms;

/// <summary>EF-backed <see cref="IAlarmStore"/>.</summary>
/// <param name="context">The bot database context.</param>
public sealed class AlarmStore(BotDbContext context) : IAlarmStore
{
    /// <inheritdoc />
    public Task<SmartAlarm> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken ct = default) =>
        // Adding is idempotent: two users can accept the same pending pairing concurrently, and the
        // unique (GuildId, ServerId, EntityId) index rejects whichever insert lands second. The empty
        // mutation is deliberate — the row the winner wrote is returned untouched, name and all —
        // and Persistord recovers the race by re-reading that winner. CreatedAt is stamped by the
        // TimestampInterceptor.
        context.SmartAlarms.UpsertAsync(
            a => a.GuildId == guildId && a.ServerId == serverId && a.EntityId == entityId,
            () => new SmartAlarm
            {
                GuildId = guildId,
                ServerId = serverId,
                EntityId = entityId,
                Name = name,
                PairedByUserId = pairedByUserId,
            },
            _ => { },
            ct);

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

        return [.. alarms.OrderBy(a => a.CreatedAt)];
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
    public Task SetReachabilityAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        DeviceReachability reachability,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a => a.Reachability = reachability, ct);

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
