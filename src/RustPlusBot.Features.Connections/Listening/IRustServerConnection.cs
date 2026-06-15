namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One live Rust+ socket to a single server, driven by one player credential.</summary>
internal interface IRustServerConnection : IAsyncDisposable
{
    /// <summary>Connects within <paramref name="timeout"/>.</summary>
    /// <param name="timeout">How long to wait for the connection.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connect outcome.</returns>
    Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a lightweight info request as a heartbeat (and auth probe), within <paramref name="timeout"/>.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The heartbeat result.</returns>
    Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
