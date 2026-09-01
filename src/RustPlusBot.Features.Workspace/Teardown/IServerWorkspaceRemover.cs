namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Public seam to remove one server: its row and its provisioned Discord resources (used by the
/// Connections removal flow).</summary>
public interface IServerWorkspaceRemover
{
    /// <summary>
    /// Deletes the server row and its category, channels, and provisioning records, holding the guild's
    /// provisioning lock across both so no in-flight reconcile can interleave and re-provision the scope.
    /// </summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a server row was deleted; false if it was already gone.</returns>
    Task<bool> RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
