namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Creates <see cref="IRustServerConnection"/>s (the real RustPlusApi adapter in production, a fake in tests).</summary>
internal interface IRustSocketSource
{
    /// <summary>Creates a connection to <paramref name="ip"/>:<paramref name="port"/> as the given player.</summary>
    /// <param name="ip">Server host.</param>
    /// <param name="port">Server Rust+ port.</param>
    /// <param name="steamId">The player's Steam64 id.</param>
    /// <param name="playerToken">The player's Rust+ token (numeric, as a string).</param>
    /// <returns>A new connection.</returns>
    IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken);
}
