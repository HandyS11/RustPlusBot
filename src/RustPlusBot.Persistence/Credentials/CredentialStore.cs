using Microsoft.EntityFrameworkCore;
using Persistord.Core;
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

        // A resubmitted token revives an Invalid credential as Standby rather than promoting it: only
        // a brand-new credential honours markActive, which is why that lives in the create branch.
        var credential = await context.PlayerCredentials.UpsertAsync(
                c => c.GuildId == request.GuildId
                     && c.RustServerId == request.RustServerId
                     && c.OwnerUserId == request.OwnerUserId,
                () => new PlayerCredential
                {
                    GuildId = request.GuildId,
                    RustServerId = request.RustServerId,
                    OwnerUserId = request.OwnerUserId,
                    Status = markActive ? CredentialStatus.Active : CredentialStatus.Standby,
                },
                row =>
                {
                    row.SteamId = request.SteamId;
                    row.ProtectedPlayerToken = protector.Protect(request.PlayerToken);
                    if (row.Status == CredentialStatus.Invalid)
                    {
                        row.Status = CredentialStatus.Standby;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

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
        // Project the affected server ids first (no token material loaded), then delete set-based.
        var serverIds = await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.OwnerUserId == ownerUserId)
            .Select(c => c.RustServerId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (serverIds.Count == 0)
        {
            return [];
        }

        await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
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
