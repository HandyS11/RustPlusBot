namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Persists and counts per-server player credentials. Tokens are protected at rest.</summary>
public interface ICredentialStore
{
    /// <summary>
    /// Inserts or refreshes the credential for (GuildId, RustServerId, OwnerUserId). A NEWLY INSERTED
    /// credential is <c>Active</c> when <paramref name="markActive"/> is true (the owner whose pairing
    /// first registered the server), otherwise <c>Standby</c>. Re-pairing refreshes the SteamId and token
    /// and resets an <c>Invalid</c> credential to <c>Standby</c> (the existing designation is preserved;
    /// <paramref name="markActive"/> is ignored on update). Returns the credential's id.
    /// </summary>
    /// <param name="request">The plaintext credential inputs.</param>
    /// <param name="markActive">When inserting, whether this credential becomes the server's active identity.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The credential's id.</returns>
    Task<Guid> UpsertFromPairingAsync(
        StoreCredentialRequest request,
        bool markActive,
        CancellationToken cancellationToken = default);

    /// <summary>Counts stored credentials for a server within a guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="rustServerId">The server to count credentials for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of stored credentials for that (guild, server).</returns>
    Task<int> CountForServerAsync(ulong guildId, Guid rustServerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every player-credential the owner holds across all servers in the guild (full account
    /// removal). Returns the distinct server ids whose pool was affected, so callers can re-evaluate
    /// those connections.
    /// </summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user whose credentials are removed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The distinct server ids whose pool changed.</returns>
    Task<IReadOnlyList<Guid>> RemoveForOwnerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the distinct server ids the owner currently holds a credential for in the guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The distinct server ids the owner has a credential for.</returns>
    Task<IReadOnlyList<Guid>> ListServerIdsForOwnerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default);
}
