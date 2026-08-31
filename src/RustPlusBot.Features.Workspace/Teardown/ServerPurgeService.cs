using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Purges one server: deletes its row, then tears down its provisioned Discord resources.</summary>
/// <param name="servers">Server management (RemoveAsync cascades all per-server rows).</param>
/// <param name="teardown">Removes the server's provisioned Discord channels/category/messages.</param>
/// <param name="provisioningLock">Held across the whole purge to block concurrent reconciliation.</param>
internal sealed class ServerPurgeService(
    IServerService servers,
    WorkspaceTeardownService teardown,
    IProvisioningLock provisioningLock) : IServerWorkspaceRemover
{
    /// <inheritdoc />
    public async Task<bool> RemoveServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        // Hold the per-guild provisioning lock across BOTH steps. A reconcile that is already in flight took
        // its "does this server exist?" decision before the row went away; if the lock were free between the
        // two it would finish re-creating the category and channels that teardown has just removed (and fault
        // on the FK when it writes its ProvisionedMessages row), leaving orphaned Discord resources behind.
        // Use the lock-free teardown core since we already hold the lock — RemoveServerAsync would deadlock
        // re-acquiring it.
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);

        // Teardown FIRST. ProvisionedCategories/Channels/Messages all declare ON DELETE CASCADE against
        // RustServers, so deleting the row first would silently take the provisioning records with it and
        // leave teardown with no Discord ids to delete — the channels would survive as orphans.
        await teardown.RemoveServerCoreAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return await servers.RemoveAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }
}
