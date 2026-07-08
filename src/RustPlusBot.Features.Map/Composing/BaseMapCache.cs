using System.Collections.Concurrent;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Caches the static-per-wipe base map per (guild, server), trying sources in order on a miss.</summary>
/// <param name="sources">Base-map sources in priority order (first hit wins).</param>
public sealed class BaseMapCache(IEnumerable<IBaseMapSource> sources)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), BaseMapImage> _images = new();

    /// <summary>Gets the cached base map, fetching from the source chain on a miss. Null results are not cached.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base map image, or null if no source can provide one.</returns>
    public async Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (_images.TryGetValue((guildId, serverId), out var cached))
        {
            return cached;
        }

        foreach (var source in sources)
        {
            var fetched = await source.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (fetched is not null)
            {
                _images[(guildId, serverId)] = fetched;
                return fetched;
            }
        }

        return null;
    }

    /// <summary>Evicts the cached base map for a server (called on disconnect).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _images.TryRemove((guildId, serverId), out _);
}
