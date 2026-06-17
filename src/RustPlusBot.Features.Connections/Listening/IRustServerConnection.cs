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

    /// <summary>Gets a server-info snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A server-info snapshot, or null on failure/timeout.</returns>
    Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets an in-game time snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An in-game time snapshot, or null on failure/timeout.</returns>
    Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets a team snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A team snapshot, or null on failure/timeout.</returns>
    Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a message to in-game team chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the send has been issued.</returns>
    /// <remarks>Unlike the probe methods, this surfaces send failures to the caller (the supervisor maps them to a failed send result).</remarks>
    Task SendTeamMessageAsync(string message, CancellationToken cancellationToken);

    /// <summary>Promotes a team member to team leader; returns true on success.</summary>
    /// <param name="steamId">Steam64 id of the member to promote.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the promotion succeeded; false on failure/timeout.</returns>
    Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Raised for every in-game team chat line received on this socket.</summary>
    event EventHandler<TeamChatLine>? TeamMessageReceived;
}
