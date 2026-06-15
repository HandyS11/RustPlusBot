using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>EF-backed <see cref="ICredentialStore"/> that protects tokens before persisting.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="protector">Protects token material before it is written.</param>
public sealed class CredentialStore(BotDbContext context, ICredentialProtector protector) : ICredentialStore
{
    /// <inheritdoc />
    public async Task<Guid> UpsertFromPairingAsync(
        StoreCredentialRequest request,
        bool markActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.PlayerCredentials
            .SingleOrDefaultAsync(
                c => c.GuildId == request.GuildId
                     && c.RustServerId == request.RustServerId
                     && c.OwnerUserId == request.OwnerUserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.SteamId = request.SteamId;
            existing.ProtectedPlayerToken = protector.Protect(request.PlayerToken);
            if (existing.Status == CredentialStatus.Invalid)
            {
                existing.Status = CredentialStatus.Standby;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }

        var credential = new PlayerCredential
        {
            GuildId = request.GuildId,
            RustServerId = request.RustServerId,
            OwnerUserId = request.OwnerUserId,
            SteamId = request.SteamId,
            ProtectedPlayerToken = protector.Protect(request.PlayerToken),
            Status = markActive ? CredentialStatus.Active : CredentialStatus.Standby,
        };

        context.PlayerCredentials.Add(credential);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return credential.Id;
    }

    /// <inheritdoc />
    public Task<int> CountForServerAsync(
        ulong guildId,
        Guid rustServerId,
        CancellationToken cancellationToken = default) =>
        context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.RustServerId == rustServerId)
            .CountAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveForOwnerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var owned = await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (owned.Count == 0)
        {
            return [];
        }

        var serverIds = owned.Select(c => c.RustServerId).Distinct().ToList();
        context.PlayerCredentials.RemoveRange(owned);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return serverIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListServerIdsForOwnerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default) =>
        await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.OwnerUserId == ownerUserId)
            .Select(c => c.RustServerId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
