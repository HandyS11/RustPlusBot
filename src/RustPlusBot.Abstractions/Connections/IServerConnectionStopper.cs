namespace RustPlusBot.Abstractions.Connections;

/// <summary>
///     Stops the live socket for one server (implemented by the connection supervisor). Exists so layers that
///     delete a server's rows can shut its connection loop down first without depending on the connections
///     feature: a loop still running when its RustServer row disappears faults on the status row's foreign key.
/// </summary>
public interface IServerConnectionStopper
{
    /// <summary>Stops the connection for one server, if running. Returns once the loop has ended.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server.</param>
    Task StopAsync(ulong guildId, Guid serverId);
}
