using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Connections;

/// <summary>
/// Persists per-server live-connection state and the operations the connection supervisor and the
/// #info renderer need. Lives in the persistence layer (returns Domain types).
/// </summary>
public interface IConnectionStore
{
    /// <summary>Gets the connection state for a server, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connection state, or null.</returns>
    Task<ConnectionState?> GetStateAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the connection state. Returns true only when the persisted (status, player count, active
    /// credential) actually changed, so callers can skip a redundant #info refresh.
    /// </summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="status">The current connection status.</param>
    /// <param name="playerCount">The current player count, or null if unknown.</param>
    /// <param name="activeCredentialId">The active credential id, or null if none.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the persisted state changed; false if it was identical.</returns>
    Task<bool> UpsertStatusAsync(
        ulong guildId,
        Guid serverId,
        ConnectionStatus status,
        int? playerCount,
        Guid? activeCredentialId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the server's <c>Active</c> player credential (with protected token), or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The active player credential, or null.</returns>
    Task<PlayerCredential?> GetActiveCredentialAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the server's full credential pool, ordered by id.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The credential pool.</returns>
    Task<IReadOnlyList<PlayerCredential>> ListPoolAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes <paramref name="credentialId"/> the server's <c>Active</c> credential and demotes the prior
    /// active to <c>Standby</c>. Returns false if the credential is not an eligible (non-Invalid) pool member.
    /// </summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="credentialId">The credential to promote to active.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the credential was promoted; false if ineligible or not found.</returns>
    Task<bool> PromoteAsync(
        ulong guildId,
        Guid serverId,
        Guid credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a credential <c>Invalid</c> (failover on auth-reject).</summary>
    /// <param name="credentialId">The credential to mark invalid.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the status has been persisted.</returns>
    Task MarkInvalidAsync(Guid credentialId, CancellationToken cancellationToken = default);

    /// <summary>Lists every <c>(GuildId, ServerId)</c> that has a non-Invalid credential (startup enumeration).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connectable (GuildId, ServerId) pairs.</returns>
    Task<IReadOnlyList<(ulong GuildId, Guid ServerId)>> ListConnectableServersAsync(
        CancellationToken cancellationToken = default);
}
