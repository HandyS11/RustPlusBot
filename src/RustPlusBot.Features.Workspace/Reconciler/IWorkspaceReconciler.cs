namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Converges a guild's Discord workspace to the desired state described by the registry.</summary>
internal interface IWorkspaceReconciler
{
    /// <summary>Reconciles the global RustPlusBot category and its channels/messages.</summary>
    /// <param name="guildId">The guild to reconcile.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reconcile result.</returns>
    Task<ReconcileResult> ReconcileGlobalAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles a single server's category and its channels/messages.</summary>
    /// <param name="guildId">The guild to reconcile.</param>
    /// <param name="serverId">The server to reconcile.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reconcile result.</returns>
    Task<ReconcileResult> ReconcileServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Converges an already-provisioned guild (global + all servers). No-op if not provisioned.</summary>
    /// <param name="guildId">The guild to heal.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task HealGuildAsync(ulong guildId, CancellationToken cancellationToken = default);
}
