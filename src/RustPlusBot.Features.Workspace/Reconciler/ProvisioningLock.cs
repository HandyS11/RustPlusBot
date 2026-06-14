using System.Collections.Concurrent;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Per-guild <see cref="SemaphoreSlim"/>-backed lock. Registered as a singleton.</summary>
internal sealed class ProvisioningLock : IProvisioningLock
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(guildId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
