using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace.Teardown;

namespace RustPlusBot.Features.Connections.Removal;

/// <summary>Default <see cref="IServerRemovalService"/>. Order matters: stop the socket before deleting the row,
/// so no late status write re-inserts a connection-state row against a deleted server (FK violation). The row
/// delete and the Discord teardown are then done together under the guild's provisioning lock, so an in-flight
/// reconcile cannot re-provision the scope between them.</summary>
/// <param name="supervisor">Stops the live socket.</param>
/// <param name="workspace">Deletes the RustServer row and tears down its Discord category/channels/records.</param>
internal sealed class ServerRemovalService(
    IConnectionSupervisor supervisor,
    IServerWorkspaceRemover workspace) : IServerRemovalService
{
    /// <inheritdoc />
    public async Task<bool> RemoveServerAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        await supervisor.StopAsync(guildId, serverId).ConfigureAwait(false);
        return await workspace.RemoveServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }
}
