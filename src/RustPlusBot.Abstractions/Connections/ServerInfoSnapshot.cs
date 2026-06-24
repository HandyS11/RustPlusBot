namespace RustPlusBot.Abstractions.Connections;

/// <summary>A point-in-time view of a server's population and wipe, decoupled from RustPlusApi types.</summary>
/// <param name="Players">Current player count.</param>
/// <param name="MaxPlayers">Server slot cap.</param>
/// <param name="QueuedPlayers">Players waiting in queue.</param>
/// <param name="WipeTimeUtc">When the server last wiped, if known.</param>
public sealed record ServerInfoSnapshot(int Players, int MaxPlayers, int QueuedPlayers, DateTimeOffset? WipeTimeUtc);
