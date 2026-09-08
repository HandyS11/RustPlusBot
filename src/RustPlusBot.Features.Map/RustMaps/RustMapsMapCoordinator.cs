using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Map;

namespace RustPlusBot.Features.Map.RustMaps;

/// <inheritdoc cref="IRustMapsMapCoordinator" />
public sealed class RustMapsMapCoordinator : IRustMapsMapCoordinator, IInfoMapReadModel
{
    private readonly ConcurrentDictionary<RustMapsMapKey, Entry> _byKey = new();

    /// <inheritdoc />
    public InfoMapResolution Resolve(ulong guildId, Guid serverId, int size, int seed)
    {
        var key = new RustMapsMapKey(size, seed);
        if (Snapshot(key) is not { State: RustMapsGenerationState.Ready, Ready: { } ready })
        {
            return InfoMapResolution.Pending;
        }

        // A render is withheld until it has been checked against this server: seed + size do not identify a
        // world, so an unverified render may be a different island (see RustMapsMapMatcher).
        return MatchFor(key, guildId, serverId) switch
        {
            RustMapsMapMatch.Match => new InfoMapResolution(InfoMapStatus.Verified,
                new InfoMapView(ready.ImageUrl, ready.RustMapsUrl)),
            RustMapsMapMatch.Mismatch => new InfoMapResolution(InfoMapStatus.Mismatched, null),
            _ => InfoMapResolution.Pending,
        };
    }

    /// <inheritdoc />
    public void SetMatch(RustMapsMapKey key, ulong guildId, Guid serverId, RustMapsMapMatch match)
    {
        if (match == RustMapsMapMatch.Unknown)
        {
            return;
        }

        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.Matches[(guildId, serverId)] = match;
        }
    }

    /// <inheritdoc />
    public RustMapsMapMatch MatchFor(RustMapsMapKey key, ulong guildId, Guid serverId)
    {
        if (!_byKey.TryGetValue(key, out var entry))
        {
            return RustMapsMapMatch.Unknown;
        }

        lock (entry.Gate)
        {
            return entry.Matches.TryGetValue((guildId, serverId), out var match)
                ? match
                : RustMapsMapMatch.Unknown;
        }
    }

    /// <inheritdoc />
    public void Register(RustMapsMapKey key, ulong guildId, Guid serverId)
    {
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.Requesters.Add((guildId, serverId));
        }
    }

    /// <inheritdoc />
    public RustMapsMapSnapshot Snapshot(RustMapsMapKey key)
    {
        if (!_byKey.TryGetValue(key, out var entry))
        {
            return new RustMapsMapSnapshot(RustMapsGenerationState.Idle, null, null);
        }

        lock (entry.Gate)
        {
            return new RustMapsMapSnapshot(entry.State, entry.MapId, entry.Ready);
        }
    }

    /// <inheritdoc />
    public bool TrySetGenerating(RustMapsMapKey key, string? mapId)
    {
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            if (entry.State is RustMapsGenerationState.Ready or RustMapsGenerationState.Failed
                or RustMapsGenerationState.LimitReached)
            {
                return false;
            }

            entry.State = RustMapsGenerationState.Generating;
            entry.MapId = mapId;
            return true;
        }
    }

    /// <inheritdoc />
    public void SetReady(RustMapsMapKey key, RustMapsReadyMap ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.State = RustMapsGenerationState.Ready;
            entry.Ready = ready;
        }
    }

    /// <inheritdoc />
    public void SetFailed(RustMapsMapKey key) => SetTerminal(key, RustMapsGenerationState.Failed);

    /// <inheritdoc />
    public void SetLimitReached(RustMapsMapKey key) => SetTerminal(key, RustMapsGenerationState.LimitReached);

    /// <inheritdoc />
    public IReadOnlyList<RustMapsMapKey> ReadyKeys() =>
        KeysWhere(static state => state is RustMapsGenerationState.Ready);

    /// <inheritdoc />
    public IReadOnlyList<RustMapsMapKey> PendingKeys() =>
        KeysWhere(static state => state is RustMapsGenerationState.Idle or RustMapsGenerationState.Generating);

    /// <inheritdoc />
    public IReadOnlyList<(ulong Guild, Guid Server)> Requesters(RustMapsMapKey key)
    {
        if (!_byKey.TryGetValue(key, out var entry))
        {
            return [];
        }

        lock (entry.Gate)
        {
            return [.. entry.Requesters];
        }
    }

    /// <summary>Keys with at least one requester whose state satisfies a predicate.</summary>
    /// <param name="statePredicate">The state filter.</param>
    private List<RustMapsMapKey> KeysWhere(Func<RustMapsGenerationState, bool> statePredicate)
    {
        var keys = new List<RustMapsMapKey>();
        foreach (var (key, entry) in _byKey)
        {
            lock (entry.Gate)
            {
                if (entry.Requesters.Count > 0 && statePredicate(entry.State))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private void SetTerminal(RustMapsMapKey key, RustMapsGenerationState state)
    {
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.State = state;
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public RustMapsGenerationState State { get; set; } = RustMapsGenerationState.Idle;
        public string? MapId { get; set; }
        public RustMapsReadyMap? Ready { get; set; }
        public HashSet<(ulong Guild, Guid Server)> Requesters { get; } = [];
        public Dictionary<(ulong Guild, Guid Server), RustMapsMapMatch> Matches { get; } = [];
    }
}
