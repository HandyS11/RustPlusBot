using System.Collections.Concurrent;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Caches the static-per-wipe base map image per (guild, server). Singleton so the cache survives across refreshes.</summary>
/// <param name="query">The live query seam used to fetch the base map on a cache miss.</param>
public sealed class BaseMapCache(IRustServerQuery query)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte[]> _images = new();

    /// <summary>Gets the cached base map, fetching and caching it on a miss. Null results are not cached.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base-map JPEG bytes, or null if unavailable.</returns>
    public async Task<byte[]?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (_images.TryGetValue((guildId, serverId), out var cached))
        {
            return cached;
        }

        var fetched = await query.GetMapImageAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (fetched is not null)
        {
            _images[(guildId, serverId)] = fetched;
        }

        return fetched;
    }

    /// <summary>Evicts the cached base map for a server (called on disconnect).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _images.TryRemove((guildId, serverId), out _);
}
