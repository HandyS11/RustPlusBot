using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>Per-(guild,server) gate that allows a refresh at most once per interval.</summary>
/// <param name="clock">Supplies the current time.</param>
internal sealed class MapRefreshThrottle(IClock clock)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), DateTimeOffset> _last = new();

    /// <summary>Returns true if a refresh is due for the key, recording the time when it returns true.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="interval">The minimum spacing between refreshes.</param>
    /// <returns>True at most once per interval per key.</returns>
    public bool ShouldRefresh(ulong guildId, Guid serverId, TimeSpan interval)
    {
        var now = clock.UtcNow;
        var key = (guildId, serverId);
        if (_last.TryGetValue(key, out var last) && now - last < interval)
        {
            return false;
        }

        _last[key] = now;
        return true;
    }
}
