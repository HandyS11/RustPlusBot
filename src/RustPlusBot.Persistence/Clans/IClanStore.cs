using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Persistence.Clans;

/// <summary>
/// Persists the latest known clan snapshot per (guild, server) and caches Steam id to display-name
/// mappings for clan members. Row presence in the underlying clan-state table is the single source
/// of truth for whether the paired player currently belongs to a clan.
/// </summary>
public interface IClanStore
{
    /// <summary>Gets the latest stored clan snapshot, or null when no clan is currently stored.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stored snapshot, or null when the server has no clan row.</returns>
    Task<ClanSnapshot?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Overwrites the stored clan snapshot for the server, creating the row if absent.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="snapshot">The snapshot to persist.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the snapshot has been persisted.</returns>
    Task SaveAsync(ulong guildId, Guid serverId, ClanSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored clan snapshot for the server, if any.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when a row actually existed and was removed; false when there was nothing to clear.</returns>
    Task<bool> ClearAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Determines whether the server currently has a stored clan snapshot.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when a clan row exists for the server.</returns>
    Task<bool> HasClanAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Looks up cached display names for the given Steam ids.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="steamIds">The Steam64 ids to resolve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A dictionary of the requested ids to their cached names. Ids with no cached name are omitted.
    /// Empty when <paramref name="steamIds"/> is empty; the database is not queried in that case.
    /// </returns>
    Task<IReadOnlyDictionary<ulong, string>> GetNamesAsync(
        ulong guildId,
        Guid serverId,
        IReadOnlyCollection<ulong> steamIds,
        CancellationToken cancellationToken = default);

    /// <summary>Records or updates the cached display name for a Steam id.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="steamId">The Steam64 id of the player.</param>
    /// <param name="name">The observed display name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the name has been persisted (or the no-op has been recognised).</returns>
    Task RecordNameAsync(
        ulong guildId,
        Guid serverId,
        ulong steamId,
        string name,
        CancellationToken cancellationToken = default);
}
