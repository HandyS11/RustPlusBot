namespace RustPlusBot.Abstractions.Connections;

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

    /// <summary>Gets the world size and seed of a connected server, or null when unavailable.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The world snapshot, or null.</returns>
    Task<WorldSnapshot?> GetWorldAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Gets the server's monuments, or an empty list when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The monuments, or an empty list when there is no live socket.</returns>
    Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken);

    /// <summary>Reads a smart switch's on/off state, or null when there is no live socket or the call fails.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True/false for on/off, or null when unavailable.</returns>
    Task<bool?> GetSmartSwitchStateAsync(ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken);

    /// <summary>Reads a smart alarm's live state and reachability (kind-aware Rust+ read).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reading; <see cref="DeviceReachability.NoResponse"/> with a null state when there is no live socket.</returns>
    Task<DeviceReading> GetSmartAlarmReadingAsync(ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken);

    /// <summary>Reads a storage monitor's contents for a (guild, server), or null when there is no live socket.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The storage-monitor entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The contents snapshot, or null when unreachable.</returns>
    Task<StorageContentsSnapshot?> GetStorageContentsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken);

    /// <summary>Sets a smart switch on/off; returns the reachability reason when the call fails or there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="value">True to turn on, false to turn off.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see cref="DeviceReachability.Reachable"/> on success; the failure reason otherwise.</returns>
    Task<DeviceReachability> SetSmartSwitchAsync(ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken cancellationToken);

    /// <summary>Strobes a smart switch; returns the reachability reason when the call fails or there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="timeoutMs">The in-game strobe duration in milliseconds.</param>
    /// <param name="value">The terminal value after strobing.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see cref="DeviceReachability.Reachable"/> on success; the failure reason otherwise.</returns>
    Task<DeviceReachability> StrobeSmartSwitchAsync(ulong guildId,
        Guid serverId,
        ulong entityId,
        int timeoutMs,
        bool value,
        CancellationToken cancellationToken);

    /// <summary>Sets the clan message of the day on a live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="motd">The new message of the day.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True on success; false when not connected or the write failed.</returns>
    Task<bool> SetClanMotdAsync(ulong guildId, Guid serverId, string motd, CancellationToken cancellationToken);
}
