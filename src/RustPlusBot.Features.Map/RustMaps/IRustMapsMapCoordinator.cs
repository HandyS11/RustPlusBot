namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Tracks RustMaps generation state per (size, seed) and the servers awaiting each map. Thread-safe singleton.</summary>
public interface IRustMapsMapCoordinator
{
    /// <summary>Registers a server's interest in a map key (idempotent; never revives a terminal state).</summary>
    /// <param name="key">The (size, seed) map key.</param>
    /// <param name="guildId">The requesting guild's id.</param>
    /// <param name="serverId">The requesting server's id.</param>
    void Register(RustMapsMapKey key, ulong guildId, Guid serverId);

    /// <summary>Gets an immutable snapshot of a key (an Idle snapshot for an unseen key).</summary>
    /// <param name="key">The (size, seed) map key.</param>
    RustMapsMapSnapshot Snapshot(RustMapsMapKey key);

    /// <summary>Moves a key to Generating with its map id; returns false if it was already terminal.</summary>
    /// <param name="key">The (size, seed) map key.</param>
    /// <param name="mapId">The RustMaps map id, when already known.</param>
    bool TrySetGenerating(RustMapsMapKey key, string? mapId);

    /// <summary>Marks a key Ready and caches its image.</summary>
    /// <param name="key">The (size, seed) map key.</param>
    /// <param name="ready">The generated image and its RustMaps page link.</param>
    void SetReady(RustMapsMapKey key, RustMapsReadyMap ready);

    /// <summary>Marks a key Failed (no retry this wipe).</summary>
    /// <param name="key">The (size, seed) map key.</param>
    void SetFailed(RustMapsMapKey key);

    /// <summary>Marks a key LimitReached (credits exhausted; no spend).</summary>
    /// <param name="key">The (size, seed) map key.</param>
    void SetLimitReached(RustMapsMapKey key);

    /// <summary>
    /// Records whether a ready render actually depicts the world one server runs. A verdict is decided once
    /// per (key, server) and never revised for that key — the map is fixed for a wipe.
    /// </summary>
    /// <param name="key">The (size, seed) map key.</param>
    /// <param name="guildId">The requesting guild's id.</param>
    /// <param name="serverId">The requesting server's id.</param>
    /// <param name="match">The verdict; <see cref="RustMapsMapMatch.Unknown"/> is not recorded.</param>
    void SetMatch(RustMapsMapKey key, ulong guildId, Guid serverId, RustMapsMapMatch match);

    /// <summary>The verdict recorded for a (key, server), or Unknown while none has been decided.</summary>
    /// <param name="key">The (size, seed) map key.</param>
    /// <param name="guildId">The requesting guild's id.</param>
    /// <param name="serverId">The requesting server's id.</param>
    RustMapsMapMatch MatchFor(RustMapsMapKey key, ulong guildId, Guid serverId);

    /// <summary>Ready keys with at least one requester (the renders awaiting or holding a match verdict).</summary>
    IReadOnlyList<RustMapsMapKey> ReadyKeys();

    /// <summary>Keys with at least one requester still in Idle or Generating.</summary>
    IReadOnlyList<RustMapsMapKey> PendingKeys();

    /// <summary>The (guild, server) pairs awaiting a key.</summary>
    /// <param name="key">The (size, seed) map key.</param>
    IReadOnlyList<(ulong Guild, Guid Server)> Requesters(RustMapsMapKey key);
}
