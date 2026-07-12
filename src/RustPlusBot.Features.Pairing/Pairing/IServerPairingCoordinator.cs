using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>The outcome of accepting a pending server pairing.</summary>
internal enum ServerPairingAcceptOutcome
{
    /// <summary>The server was created; the full registration cycle was triggered.</summary>
    Added = 0,

    /// <summary>The server already existed (race); the credential was upserted, no event fired.</summary>
    AlreadyAdded = 1,

    /// <summary>No pending pairing was held (restart or double-click); nothing was persisted.</summary>
    Expired = 2,
}

/// <summary>Coordinates the "Add this server?" #setup prompt for new-server pairings.</summary>
internal interface IServerPairingCoordinator
{
    /// <summary>Handles a new-server pairing: prompt in #setup and hold the notification pending.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ownerUserId">The Discord user whose connected account produced the pairing.</param>
    /// <param name="notification">The transient pairing notification (holds the Rust+ token).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task HandleDetectedAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken);

    /// <summary>Accepts a pending pairing: persist server + credential and trigger registration. Race-guarded.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The accept outcome.</returns>
    Task<ServerPairingAcceptOutcome> TryAcceptAsync(
        ulong guildId,
        string ip,
        int port,
        CancellationToken cancellationToken);

    /// <summary>Drops a pending pairing; returns whether one was held.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <returns>True when a pending pairing was removed.</returns>
    bool TryDismiss(ulong guildId, string ip, int port);
}
