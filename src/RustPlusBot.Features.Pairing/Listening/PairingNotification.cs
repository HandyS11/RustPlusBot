namespace RustPlusBot.Features.Pairing.Listening;

/// <summary>Whether a pairing notification is for a server or an in-game entity.</summary>
internal enum PairingKind
{
    /// <summary>A server pairing (registers a server).</summary>
    Server = 0,

    /// <summary>An entity pairing (switch/alarm/camera) — ignored in 1b-i.</summary>
    Entity = 1,
}

/// <summary>The outcome of an initial FCM connect attempt.</summary>
internal enum PairingConnectOutcome
{
    /// <summary>Connected and checked in.</summary>
    Connected = 0,

    /// <summary>FCM rejected the credentials.</summary>
    Rejected = 1,

    /// <summary>No result within the probe window (network); retried in the background.</summary>
    Timeout = 2,
}

/// <summary>A pairing notification delivered by a listener.</summary>
/// <param name="Kind">Server or entity.</param>
/// <param name="ServerName">The server's display name.</param>
/// <param name="Ip">The server host or ip.</param>
/// <param name="Port">The Rust+ app port.</param>
/// <param name="PlayerId">The paired player's Steam64 id.</param>
/// <param name="PlayerToken">The Rust+ player token for this (server, player).</param>
internal sealed record PairingNotification(
    PairingKind Kind,
    string ServerName,
    string Ip,
    int Port,
    ulong PlayerId,
    string PlayerToken);
