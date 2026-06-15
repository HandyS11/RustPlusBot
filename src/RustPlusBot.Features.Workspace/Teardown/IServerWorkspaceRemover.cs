namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Public seam to remove one server's provisioned Discord resources (used by the Connections removal flow).</summary>
public interface IServerWorkspaceRemover
{
    /// <summary>Deletes one server's category, channels, and provisioning records.</summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
