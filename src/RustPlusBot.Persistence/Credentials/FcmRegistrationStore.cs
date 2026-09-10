using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>EF-backed <see cref="IFcmRegistrationStore"/> that protects credentials before persisting.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="protector">Protects credential material before it is written.</param>
public sealed class FcmRegistrationStore(BotDbContext context, ICredentialProtector protector)
    : IFcmRegistrationStore
{
    /// <inheritdoc />
    public async Task<Guid> UpsertAsync(
        ulong guildId,
        ulong ownerUserId,
        string fcmCredentialsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fcmCredentialsJson);

        // Persistord's upsert owns the (guild, owner) unique-index race: it re-reads the winner once
        // and applies the same mutation to it. UpdatedAt is stamped by the TimestampInterceptor.
        var registration = await context.FcmRegistrations.UpsertAsync(
                r => r.GuildId == guildId && r.OwnerUserId == ownerUserId,
                () => new FcmRegistration
                {
                    GuildId = guildId, OwnerUserId = ownerUserId
                },
                row =>
                {
                    row.ProtectedFcmCredentials = protector.Protect(fcmCredentialsJson);
                    row.Status = FcmRegistrationStatus.Active;
                },
                cancellationToken)
            .ConfigureAwait(false);

        return registration.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FcmRegistration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await context.FcmRegistrations
            .Where(r => r.Status == FcmRegistrationStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SetStatusAsync(
        Guid id,
        FcmRegistrationStatus status,
        CancellationToken cancellationToken = default)
    {
        var registration = await context.FcmRegistrations
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (registration is null)
        {
            return;
        }

        registration.Status = status;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<FcmRegistration?> GetAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default) =>
        context.FcmRegistrations
            .SingleOrDefaultAsync(r => r.GuildId == guildId && r.OwnerUserId == ownerUserId, cancellationToken);
}
