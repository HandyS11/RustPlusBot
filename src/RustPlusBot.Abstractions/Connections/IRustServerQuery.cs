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

    /// <summary>Gets a team snapshot, or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A team snapshot, or null when there is no live socket.</returns>
    Task<TeamInfoSnapshot?> GetTeamInfoAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Promotes a team member to team leader; returns false when there is no live socket or the API fails.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="steamId">Steam64 id of the member to promote.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if promoted; false when no live socket or the API fails.</returns>
    Task<bool> PromoteToLeaderAsync(ulong guildId, Guid serverId, ulong steamId, CancellationToken cancellationToken);

    /// <summary>Gets the base map image (JPEG bytes), or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base-map JPEG bytes, or null when there is no live socket.</returns>
    Task<byte[]?> GetMapImageAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Gets the static map dimensions (for grid rendering), or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The map dimensions, or null when there is no live socket.</returns>
    Task<MapDimensions?> GetMapDimensionsAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
