namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Reads live data from a connected server's socket (implemented by the connection supervisor).</summary>
public interface IRustServerQuery
{
    /// <summary>Gets server info, or null when (guildId, serverId) has no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A server-info snapshot, or null when there is no live socket.</returns>
    Task<ServerInfoSnapshot?> GetServerInfoAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Gets in-game time, or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An in-game time snapshot, or null when there is no live socket.</returns>
    Task<ServerTimeSnapshot?> GetTimeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
