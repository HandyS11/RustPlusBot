using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Events.Classifying;

namespace RustPlusBot.Features.Events.State;

/// <summary>In-memory per-(guild, server) active markers and a bounded recent-event ring. Cleared on disconnect.</summary>
/// <param name="clock">Stamps when markers become active.</param>
internal sealed class EventStateStore(IClock clock) : IEventState
{
    private const int RecentCapacity = 10;
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ServerState> _byServer = new();

    /// <inheritdoc />
    public IReadOnlyList<ActiveMarker> GetActiveMarkers(ulong guildId, Guid serverId, MarkerKind kind)
    {
        if (!_byServer.TryGetValue((guildId, serverId), out var state))
        {
            return [];
        }

        lock (state.Gate)
        {
            return
            [
                .. state.Active.Values
                    .Where(m => m.Kind == kind)
                    .OrderByDescending(m => m.SeenAtUtc)
            ];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RustMapEvent> GetRecentEvents(ulong guildId, Guid serverId)
    {
        if (!_byServer.TryGetValue((guildId, serverId), out var state))
        {
            return [];
        }

        lock (state.Gate)
        {
            return [.. state.Recent]; // already newest-first
        }
    }

    /// <summary>Applies one marker-change delta and its classified events.</summary>
    /// <param name="delta">The raw marker delta (drives the active set).</param>
    /// <param name="events">The classified events (pushed onto the recent ring).</param>
    public void Apply(MapMarkersChangedEvent delta, IReadOnlyList<RustMapEvent> events)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ArgumentNullException.ThrowIfNull(events);
        var state = _byServer.GetOrAdd((delta.GuildId, delta.ServerId), static _ => new ServerState());
        var now = clock.UtcNow;
        lock (state.Gate)
        {
            foreach (var m in delta.Added)
            {
                state.Active[m.Id] = new ActiveMarker(m.Id, m.Kind, m.X, m.Y, delta.Dimensions, now);
            }

            foreach (var m in delta.Removed)
            {
                state.Active.Remove(m.Id);
            }

            foreach (var e in events)
            {
                state.Recent.Insert(0, e);
            }

            if (state.Recent.Count > RecentCapacity)
            {
                state.Recent.RemoveRange(RecentCapacity, state.Recent.Count - RecentCapacity);
            }
        }
    }

    /// <summary>Clears all state for a server (called when its connection drops).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _byServer.TryRemove((guildId, serverId), out _);

    private sealed class ServerState
    {
        public object Gate { get; } = new();

        public Dictionary<ulong, ActiveMarker> Active { get; } = [];

        public List<RustMapEvent> Recent { get; } = [];
    }
}
