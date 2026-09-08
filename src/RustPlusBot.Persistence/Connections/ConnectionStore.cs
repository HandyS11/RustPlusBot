using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Connections;

/// <summary>EF-backed <see cref="IConnectionStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the update timestamp.</param>
public sealed class ConnectionStore(BotDbContext context, IClock clock) : IConnectionStore
{
    /// <inheritdoc />
    public Task<ConnectionState?> GetStateAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        context.ConnectionStates
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.RustServerId == serverId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectionState>> GetStatesForGuildAsync(
        ulong guildId,
        CancellationToken cancellationToken = default) =>
        await context.ConnectionStates
            .Where(s => s.GuildId == guildId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> UpsertStatusAsync(
        ulong guildId,
        Guid serverId,
        ConnectionStatus status,
        int? playerCount,
        Guid? activeCredentialId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ConnectionStates
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.RustServerId == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // The row is FK'd to RustServers with ON DELETE CASCADE, so a deleted server takes its status
            // row with it. A connection loop still running at that moment would insert a fresh row against
            // the missing parent and die on the constraint violation. A server that is gone has no status
            // to record: report "no change" rather than faulting the caller.
            if (!await ServerExistsAsync().ConfigureAwait(false))
            {
                return false;
            }

            var added = context.ConnectionStates.Add(new ConnectionState
            {
                RustServerId = serverId,
                GuildId = guildId,
                Status = status,
                PlayerCount = playerCount,
                ActiveCredentialId = activeCredentialId,
                UpdatedAt = clock.UtcNow,
            });

            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // The check above and this insert are two round trips, so the server can still be deleted
                // in between. Drop the doomed entity (it would be retried by the next SaveChanges on this
                // context) and re-check: only a vanished parent is expected here, so anything else — a
                // genuine store failure — must still surface to the caller.
                added.State = EntityState.Detached;
                if (await ServerExistsAsync().ConfigureAwait(false))
                {
                    throw;
                }

                return false;
            }

            return true;
        }

        if (existing.Status == status
            && existing.PlayerCount == playerCount
            && existing.ActiveCredentialId == activeCredentialId)
        {
            return false;
        }

        existing.Status = status;
        existing.PlayerCount = playerCount;
        existing.ActiveCredentialId = activeCredentialId;
        existing.UpdatedAt = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;

        Task<bool> ServerExistsAsync() => context.RustServers
            .AnyAsync(s => s.Id == serverId && s.GuildId == guildId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PlayerCredential?> GetActiveCredentialAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        context.PlayerCredentials.SingleOrDefaultAsync(
            c => c.GuildId == guildId && c.RustServerId == serverId && c.Status == CredentialStatus.Active,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerCredential>> ListPoolAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> PromoteAsync(
        ulong guildId,
        Guid serverId,
        Guid credentialId,
        CancellationToken cancellationToken = default)
    {
        var pool = await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var target = pool.SingleOrDefault(c => c.Id == credentialId);
        if (target is null || target.Status == CredentialStatus.Invalid)
        {
            return false;
        }

        foreach (var credential in pool)
        {
            if (credential.Status == CredentialStatus.Active && credential.Id != credentialId)
            {
                credential.Status = CredentialStatus.Standby;
            }
        }

        target.Status = CredentialStatus.Active;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task MarkInvalidAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var credential = await context.PlayerCredentials
            .SingleOrDefaultAsync(c => c.Id == credentialId, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return;
        }

        credential.Status = CredentialStatus.Invalid;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(ulong GuildId, Guid ServerId)>> ListConnectableServersAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.PlayerCredentials
            .Where(c => c.Status != CredentialStatus.Invalid)
            .Select(c => new
            {
                c.GuildId, c.RustServerId
            })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ConvertAll(r => (r.GuildId, r.RustServerId));
    }
}
