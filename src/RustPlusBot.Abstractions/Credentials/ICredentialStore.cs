namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Persists and counts per-server player credentials. Tokens are protected at rest.</summary>
public interface ICredentialStore
{
    /// <summary>
    /// Inserts or refreshes the credential for (GuildId, RustServerId, OwnerUserId). The first credential
    /// stored for a server becomes <c>Active</c>; subsequent owners are <c>Standby</c>. Re-pairing refreshes
    /// the SteamId and token and resets an <c>Invalid</c> credential to <c>Standby</c>. Returns the credential's id.
    /// </summary>
    /// <param name="request">The plaintext credential inputs.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The credential's id.</returns>
    Task<Guid> UpsertFromPairingAsync(StoreCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts stored credentials for a server within a guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="rustServerId">The server to count credentials for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of stored credentials for that (guild, server).</returns>
    Task<int> CountForServerAsync(ulong guildId, Guid rustServerId, CancellationToken cancellationToken = default);
}
