using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>
/// Persists per-user FCM listener registrations. Credentials are protected at rest. Lives in the
/// persistence layer (not Abstractions) because it returns the Domain <see cref="FcmRegistration"/>
/// type, and Abstractions has no Domain reference.
/// </summary>
public interface IFcmRegistrationStore
{
    /// <summary>Inserts or refreshes the registration for (guild, owner), setting it <c>Active</c>. Returns its id.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user connecting credentials.</param>
    /// <param name="fcmCredentialsJson">The plaintext FCM credentials JSON (protected before storage).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The registration's id.</returns>
    Task<Guid> UpsertAsync(
        ulong guildId,
        ulong ownerUserId,
        string fcmCredentialsJson,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every <c>Active</c> registration across all guilds (used at startup).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The active registrations.</returns>
    Task<IReadOnlyList<FcmRegistration>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a registration's status.</summary>
    /// <param name="id">The registration id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the status has been persisted.</returns>
    Task SetStatusAsync(Guid id, FcmRegistrationStatus status, CancellationToken cancellationToken = default);

    /// <summary>Gets the registration for (guild, owner), or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The registration, or null.</returns>
    Task<FcmRegistration?> GetAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
}
