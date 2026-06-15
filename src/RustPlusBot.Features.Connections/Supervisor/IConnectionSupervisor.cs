namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Owns the live Rust+ sockets — one per (guild, server).</summary>
internal interface IConnectionSupervisor
{
    /// <summary>Starts a connection for every server that has a non-Invalid credential (called once at startup).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts (or restarts) the connection for one server (after a swap or on server registration).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task EnsureConnectionAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Stops the connection for one server, if running.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server.</param>
    Task StopAsync(ulong guildId, Guid serverId);

    /// <summary>Cancels and disposes every connection (called on shutdown).</summary>
    Task StopAllAsync();
}
