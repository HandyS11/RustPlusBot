using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Caches the small set of provisioned #teamchat channels (rebuilt when the cache goes stale) and resolves
/// both directions. The Discord MessageReceived handler calls <see cref="ResolveAsync"/> for every guild
/// message, so a per-call DB hit is avoided by serving hits AND misses from the cached snapshot.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class TeamChatChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : ITeamChatChannelLocator, IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;

    private Dictionary<ulong, (ulong GuildId, Guid ServerId)> _byChannelId = new();

    private Dictionary<(ulong GuildId, Guid ServerId), ulong> _byServer = new();

    /// <inheritdoc />
    public void Dispose() => _refreshGate.Dispose();

    /// <inheritdoc />
    public async Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byServer.TryGetValue((guildId, serverId), out var id) ? id : null;
    }

    /// <inheritdoc />
    public async Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId,
        CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byChannelId.TryGetValue(channelId, out var pair) ? pair : null;
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
                var rows = await store.GetChannelsByKeyAsync(WorkspaceChannelKeys.ServerTeamChat, cancellationToken)
                    .ConfigureAwait(false);

                var byChannel = new Dictionary<ulong, (ulong GuildId, Guid ServerId)>();
                var byServer = new Dictionary<(ulong GuildId, Guid ServerId), ulong>();
                foreach (var row in rows)
                {
                    if (row.RustServerId is not { } serverId)
                    {
                        continue;
                    }

                    byChannel[row.DiscordChannelId] = (row.GuildId, serverId);
                    byServer[(row.GuildId, serverId)] = row.DiscordChannelId;
                }

                _byChannelId = byChannel;
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
