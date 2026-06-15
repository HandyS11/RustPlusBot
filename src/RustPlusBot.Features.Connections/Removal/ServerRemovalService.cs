using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Removal;

/// <summary>Default <see cref="IServerRemovalService"/>. Order matters: stop the socket before deleting the row,
/// so no late status write re-inserts a connection-state row against a deleted server (FK violation).</summary>
/// <param name="supervisor">Stops the live socket.</param>
/// <param name="servers">Deletes the RustServer (cascades credentials + connection state).</param>
/// <param name="workspace">Tears down the server's Discord category/channels/records.</param>
internal sealed class ServerRemovalService(
    IConnectionSupervisor supervisor,
    IServerService servers,
    IServerWorkspaceRemover workspace) : IServerRemovalService
{
    /// <inheritdoc />
    public async Task<bool> RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        await supervisor.StopAsync(guildId, serverId).ConfigureAwait(false);
        var removed = await servers.RemoveAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        await workspace.RemoveServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return removed;
    }
}
