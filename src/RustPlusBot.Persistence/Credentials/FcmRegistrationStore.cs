using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>EF-backed <see cref="IFcmRegistrationStore"/> that protects credentials before persisting.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="protector">Protects credential material before it is written.</param>
/// <param name="clock">Supplies the update timestamp.</param>
public sealed class FcmRegistrationStore(BotDbContext context, ICredentialProtector protector, IClock clock)
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

        var existing = await context.FcmRegistrations
            .SingleOrDefaultAsync(r => r.GuildId == guildId && r.OwnerUserId == ownerUserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.ProtectedFcmCredentials = protector.Protect(fcmCredentialsJson);
            existing.Status = FcmRegistrationStatus.Active;
            existing.UpdatedAt = clock.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }

        var registration = new FcmRegistration
        {
            GuildId = guildId,
            OwnerUserId = ownerUserId,
            ProtectedFcmCredentials = protector.Protect(fcmCredentialsJson),
            Status = FcmRegistrationStatus.Active,
            UpdatedAt = clock.UtcNow,
        };

        context.FcmRegistrations.Add(registration);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        registration.UpdatedAt = clock.UtcNow;
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
