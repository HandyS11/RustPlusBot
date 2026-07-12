using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Caches the provisioned global #setup channel per guild. Sibling of <see cref="CachingChannelLocator"/>;
/// separate because #setup rows carry a null <c>RustServerId</c>, which the per-server base skips.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class SetupChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : ISetupChannelLocator, IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private Dictionary<ulong, ulong> _byGuild = [];

    /// <inheritdoc />
    public void Dispose() => _refreshGate.Dispose();

    /// <inheritdoc />
    public async Task<ulong?> GetChannelIdAsync(ulong guildId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byGuild.TryGetValue(guildId, out var id) ? id : null;
    }

    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
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
                var rows = await store.GetChannelsByKeyAsync(WorkspaceChannelKeys.Setup, cancellationToken)
                    .ConfigureAwait(false);

                Dictionary<ulong, ulong> byGuild = [];
                foreach (var row in rows)
                {
                    // #setup is global-scope: only rows without a server id belong to it.
                    if (row.RustServerId is not null)
                    {
                        continue;
                    }

                    byGuild[row.GuildId] = row.DiscordChannelId;
                }

                _byGuild = byGuild;
                _builtAt = clock.UtcNow;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
