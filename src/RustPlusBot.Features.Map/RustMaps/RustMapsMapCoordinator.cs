using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Map;

namespace RustPlusBot.Features.Map.RustMaps;

/// <inheritdoc cref="IRustMapsMapCoordinator" />
public sealed class RustMapsMapCoordinator : IRustMapsMapCoordinator, IInfoMapReadModel
{
    private readonly ConcurrentDictionary<RustMapsMapKey, Entry> _byKey = new();

    /// <inheritdoc />
    public InfoMapView? GetReady(int size, int seed)
    {
        var snap = Snapshot(new RustMapsMapKey(size, seed));
        return snap is { State: RustMapsGenerationState.Ready, Ready: { } r }
            ? new InfoMapView(r.ImageUrl, r.RustMapsUrl)
            : null;
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
    public IReadOnlyList<RustMapsMapKey> PendingKeys()
    {
        var pending = new List<RustMapsMapKey>();
        foreach (var (key, entry) in _byKey)
        {
            lock (entry.Gate)
            {
                if (entry.Requesters.Count > 0
                    && entry.State is RustMapsGenerationState.Idle or RustMapsGenerationState.Generating)
                {
                    pending.Add(key);
                }
            }
        }

        return pending;
    }

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
    }
}
