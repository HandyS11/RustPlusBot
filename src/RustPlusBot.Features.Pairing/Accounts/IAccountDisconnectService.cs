namespace RustPlusBot.Features.Pairing.Accounts;

/// <summary>Full account disconnect: stop the listener, disable the registration, drop the user's pool credentials.</summary>
internal interface IAccountDisconnectService
{
    /// <summary>Reads what a disconnect would affect, for the confirmation dialog.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Whether the user is connected and the names of servers they would be removed from.</returns>
    Task<AccountDisconnectPreview> PreviewAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the user's FCM listener, marks their registration Disabled, removes all their pool credentials
    /// in the guild, and publishes a <c>ServerCredentialsChangedEvent</c> per affected server.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of servers the user was removed from.</returns>
    Task<int> DisconnectAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
}

/// <summary>What an account disconnect would affect.</summary>
/// <param name="IsConnected">True if the user has a non-Disabled registration or any pool credential.</param>
/// <param name="AffectedServerNames">Names of servers the user holds a credential for.</param>
internal sealed record AccountDisconnectPreview(bool IsConnected, IReadOnlyList<string> AffectedServerNames);
