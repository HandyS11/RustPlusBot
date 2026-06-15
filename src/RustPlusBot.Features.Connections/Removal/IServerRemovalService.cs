namespace RustPlusBot.Features.Connections.Removal;

/// <summary>Orchestrates removing a server: stop the socket, delete the row (cascades), tear down the workspace.</summary>
internal interface IServerRemovalService
{
    /// <summary>Removes a server end-to-end. Returns true if a server row was deleted.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a matching server row was removed.</returns>
    Task<bool> RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
