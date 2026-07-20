namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>
///     Re-renders a server's #info messages in place without re-walking channel provisioning.
///     The full <see cref="IWorkspaceReconciler.ReconcileServerAsync" /> takes the per-guild
///     provisioning lock and re-checks every category and channel — far too heavy for a
///     minute-by-minute pulse. This path reads the message ids straight from the store and edits.
/// </summary>
internal interface IServerInfoRefresher
{
    /// <summary>Re-renders the server's #info messages, editing only those whose render changed.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every message has been considered.</returns>
    Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
