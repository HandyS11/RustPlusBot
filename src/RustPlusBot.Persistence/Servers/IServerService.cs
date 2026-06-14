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
    Task<RustServer> AddAsync(ulong guildId, ulong addedByUserId, string name, string ip, int port,
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
}
