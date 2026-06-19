using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Servers;

/// <summary>Guild-scoped management of Rust+ server targets.</summary>
public interface IServerService
{
    /// <summary>Adds a server to a guild and returns it.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="addedByUserId">The Discord user adding the server.</param>
    /// <param name="name">Display name.</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created server.</returns>
    Task<RustServer> AddAsync(ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a guild's servers, ordered by name.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The guild's servers.</returns>
    Task<IReadOnlyList<RustServer>> ListAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Removes a server by id within a guild. Returns true if a row was removed.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The server id to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a matching server was removed; otherwise false.</returns>
    Task<bool> RemoveAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Gets a server by id within a guild, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server, or null if not found in this guild.</returns>
    Task<RustServer?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Looks up a server by endpoint without creating one; returns null if absent.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching server, or null.</returns>
    Task<RustServer?> GetByEndpointAsync(ulong guildId,
        string ip,
        int port,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up a server by its Facepunch server GUID within a guild without creating one; returns null if absent.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="facepunchServerId">The Facepunch server GUID delivered by FCM pairings.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching server, or null.</returns>
    Task<RustServer?> GetByFacepunchServerIdAsync(ulong guildId,
        Guid facepunchServerId,
        CancellationToken cancellationToken = default);

    /// <summary>Backfills a server's Facepunch server GUID by id; idempotent no-op when the server is absent or already equal.</summary>
    /// <param name="serverId">The local server id to update.</param>
    /// <param name="facepunchServerId">The Facepunch server GUID to store.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the backfill is persisted or skipped.</returns>
    Task SetFacepunchServerIdAsync(Guid serverId,
        Guid facepunchServerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the server matching (guild, ip, port), creating it if none exists. Handles the
    /// concurrent-create race (two pairings for the same new server) by re-reading the winner.
    /// </summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="addedByUserId">The Discord user whose pairing created it (only used on create).</param>
    /// <param name="name">Display name (only used on create).</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved server and whether it was newly created.</returns>
    Task<(RustServer Server, bool Created)> ResolveOrCreateByEndpointAsync(
        ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default);
}
