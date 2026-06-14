namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Persists and counts player credentials. Tokens are protected at rest.</summary>
public interface ICredentialStore
{
    /// <summary>Stores a credential (as Standby) and returns its new id.</summary>
    /// <param name="request">The plaintext credential inputs.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new credential's id.</returns>
    Task<Guid> StoreAsync(StoreCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts stored credentials for a server within a guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="rustServerId">The server to count credentials for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of stored credentials for that (guild, server).</returns>
    Task<int> CountForServerAsync(ulong guildId, Guid rustServerId, CancellationToken cancellationToken = default);
}
