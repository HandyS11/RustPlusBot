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

        var isFirstForServer = !await context.PlayerCredentials
            .AnyAsync(c => c.GuildId == request.GuildId && c.RustServerId == request.RustServerId, cancellationToken)
            .ConfigureAwait(false);

        var credential = new PlayerCredential
        {
            GuildId = request.GuildId,
            RustServerId = request.RustServerId,
            OwnerUserId = request.OwnerUserId,
            SteamId = request.SteamId,
            ProtectedPlayerToken = protector.Protect(request.PlayerToken),
            Status = isFirstForServer ? CredentialStatus.Active : CredentialStatus.Standby,
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
}
