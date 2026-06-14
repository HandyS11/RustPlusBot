namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Serializes reconciliation per guild so concurrent triggers cannot interleave.</summary>
internal interface IProvisioningLock
{
    /// <summary>Acquires the guild's lock; dispose the result to release.</summary>
    /// <param name="guildId">The guild to lock.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A disposable handle that releases the lock on dispose.</returns>
    Task<IDisposable> AcquireAsync(ulong guildId, CancellationToken cancellationToken = default);
}
