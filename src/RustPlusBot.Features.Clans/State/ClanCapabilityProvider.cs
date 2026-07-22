using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.State;

/// <summary>
/// Reports the "clan" capability as available exactly while a clan snapshot is stored for the
/// server, which is what gates the two clan channels.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped clan store.</param>
/// <param name="clock">Drives the short-lived answer cache.</param>
internal sealed class ClanCapabilityProvider(IServiceScopeFactory scopeFactory, IClock clock)
    : IWorkspaceCapabilityProvider
{
    /// <summary>
    /// How long an answer is reused. Two gated specs mean two probes per server per reconcile, and
    /// the heal path reconciles every server on a timer; this is kept well under the reconcile
    /// interval so a clan transition is still picked up promptly.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<(ulong GuildId, Guid ServerId), (DateTimeOffset At, bool Available)> _cache =
        new();

    /// <inheritdoc />
    public string Capability => WorkspaceCapabilities.Clan;

    /// <summary>
    /// Drops the cached answer for a server. Called by <see cref="ClanStateService"/> immediately
    /// before it reconciles a clan transition, so the reconcile sees the just-written row rather
    /// than a stale pre-transition answer.
    /// </summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    public void Invalidate(ulong guildId, Guid serverId) => _cache.TryRemove((guildId, serverId), out _);

    /// <inheritdoc />
    public async ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken)
    {
        // Clans are per-server; the global scope never has clan channels.
        if (serverId is not { } id)
        {
            return false;
        }

        var now = clock.UtcNow;
        if (_cache.TryGetValue((guildId, id), out var cached) && now - cached.At < CacheTtl)
        {
            return cached.Available;
        }

        // No try/catch: a store failure must fault the reconcile rather than be swallowed into a
        // "false" that would delete the clan channels and their history.
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IClanStore>();
            var available = await store.HasClanAsync(guildId, id, cancellationToken).ConfigureAwait(false);
            _cache[(guildId, id)] = (now, available);
            return available;
        }
    }
}
