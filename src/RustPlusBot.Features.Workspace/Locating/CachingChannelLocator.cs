using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Caches a provisioned channel set per key and resolves (guild, server) → channel id.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
/// <param name="channelKey">The workspace channel key to load from the store.</param>
internal abstract class CachingChannelLocator(IServiceScopeFactory scopeFactory, IClock clock, string channelKey)
    : IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private Dictionary<(ulong GuildId, Guid ServerId), ulong> _byServer = new();

    /// <summary>A read-only view of the cached (guild, server) → channel-id map, available after ensure-fresh.</summary>
    protected IReadOnlyDictionary<(ulong GuildId, Guid ServerId), ulong> Entries => _byServer;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshGate.Dispose();
        }
    }

    /// <summary>
    /// Gets the Discord channel id for (<paramref name="guildId"/>, <paramref name="serverId"/>), or
    /// <see langword="null"/> if no channel is provisioned.
    /// </summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or <see langword="null"/> if not provisioned.</returns>
    public async Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byServer.TryGetValue((guildId, serverId), out var id) ? id : null;
    }

    /// <summary>Ensures the cache is current; refreshes from the store when the TTL has elapsed.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (clock.UtcNow - _builtAt < CacheTtl)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (clock.UtcNow - _builtAt < CacheTtl)
            {
                return;
            }

            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                var rows = await store.GetChannelsByKeyAsync(channelKey, cancellationToken).ConfigureAwait(false);

                var byServer = new Dictionary<(ulong GuildId, Guid ServerId), ulong>();
                foreach (var row in rows)
                {
                    if (row.RustServerId is not { } serverId)
                    {
                        continue;
                    }

                    byServer[(row.GuildId, serverId)] = row.DiscordChannelId;
                }

                _byServer = byServer;
                _builtAt = clock.UtcNow;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
