using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.State;

/// <summary>One timed boundary an oil rig crossed, ready to publish as a <see cref="RigStateChangedEvent"/>.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Rig">Which rig.</param>
/// <param name="Kind">The boundary crossed (CrateLootable or Respawned).</param>
/// <param name="X">The rig monument's world X coordinate.</param>
/// <param name="Y">The rig monument's world Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
public readonly record struct RigCrossing(
    ulong GuildId,
    Guid ServerId,
    RigKind Rig,
    RigEventKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions);

/// <summary>
/// In-memory per-(guild, server, rig) oil-rig state machine. An untracked rig is Online by default;
/// a rig enters the map on its first <see cref="Apply"/> (Activated) and is dropped again once it
/// respawns back to Online. Timed transitions are driven by <see cref="Advance"/> (the tick) and
/// also settled lazily on <see cref="Get"/>.
/// </summary>
/// <param name="clock">Supplies the current time.</param>
/// <param name="options">Supplies the rig phase windows.</param>
public sealed class RigStateStore(IClock clock, IOptions<ConnectionOptions> options) : IRigState
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server, RigKind Rig), Entry> _rigs = new();

    private object Gate { get; } = new();

    /// <inheritdoc />
    public RigState Get(ulong guildId, Guid serverId, RigKind rig)
    {
        var now = clock.UtcNow;
        var key = (guildId, serverId, rig);
        lock (Gate)
        {
            if (!_rigs.TryGetValue(key, out var entry))
            {
                return new RigState(RigStatus.Online, null);
            }

            // Settle any overdue transitions (without emitting crossings — Advance does that).
            while (TrySettle(key, entry, now, out var next))
            {
                if (next is null)
                {
                    return new RigState(RigStatus.Online, null); // respawned -> dropped
                }

                entry = next;
            }

            var remaining = Remaining(entry, now);
            return new RigState(entry.Status, remaining);
        }
    }

    /// <summary>Sets a rig Active (the CH47 ground-truth override from any state).</summary>
    /// <param name="activated">The activation event; its <see cref="RigStateChangedEvent.Kind"/> must be <see cref="RigEventKind.Activated"/>.</param>
    /// <exception cref="ArgumentException">The event's kind is not <see cref="RigEventKind.Activated"/> (the timed kinds are produced by <see cref="Advance"/>, not applied here).</exception>
    public void Apply(RigStateChangedEvent activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        if (activated.Kind != RigEventKind.Activated)
        {
            throw new ArgumentException(
                $"Apply only accepts {nameof(RigEventKind.Activated)} events; got {activated.Kind}.",
                nameof(activated));
        }

        var key = (activated.GuildId, activated.ServerId, activated.Rig);
        lock (Gate)
        {
            _rigs[key] = new Entry(RigStatus.Active, clock.UtcNow, activated.X, activated.Y, activated.Dimensions);
        }
    }

    /// <summary>Advances every tracked rig whose timed window has elapsed, returning the crossings to publish.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The boundary crossings (CrateLootable / Respawned) since the last advance.</returns>
    public IReadOnlyList<RigCrossing> Advance(DateTimeOffset now)
    {
        var crossings = new List<RigCrossing>();
        lock (Gate)
        {
            foreach (var key in _rigs.Keys.ToList())
            {
                if (!_rigs.TryGetValue(key, out var entry))
                {
                    continue;
                }

                while (TrySettle(key, entry, now, out var next, recordInto: crossings))
                {
                    if (next is null)
                    {
                        break; // dropped
                    }

                    entry = next;
                }
            }
        }

        return crossings;
    }

    /// <summary>Drops all rig state for a server (on disconnect).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId)
    {
        lock (Gate)
        {
            foreach (var key in _rigs.Keys.Where(k => k.Guild == guildId && k.Server == serverId).ToList())
            {
                _rigs.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Performs one due transition. Returns true if a transition happened; sets <paramref name="next"/>
    /// to the new entry, or null if the rig respawned (and was dropped). Optionally records the crossing.</summary>
    /// <param name="key">The rig key to settle.</param>
    /// <param name="entry">The current entry for the rig.</param>
    /// <param name="now">The current time used to determine if a transition is due.</param>
    /// <param name="next">The updated entry after transition, or null if the rig was dropped.</param>
    /// <param name="recordInto">If non-null, the crossing is appended here.</param>
    /// <returns><see langword="true"/> if a transition occurred; <see langword="false"/> if none was due.</returns>
    private bool TrySettle(
        (ulong Guild, Guid Server, RigKind Rig) key,
        Entry entry,
        DateTimeOffset now,
        out Entry? next,
        List<RigCrossing>? recordInto = null)
    {
        var opts = options.Value;
        switch (entry.Status)
        {
            case RigStatus.Active when now - entry.PhaseStart >= opts.RigActiveWindow:
                next = entry with
                {
                    Status = RigStatus.Offline, PhaseStart = entry.PhaseStart + opts.RigActiveWindow
                };
                _rigs[key] = next;
                recordInto?.Add(new RigCrossing(key.Guild, key.Server, key.Rig, RigEventKind.CrateLootable,
                    entry.X, entry.Y, entry.Dimensions));
                return true;
            case RigStatus.Offline when now - entry.PhaseStart >= opts.RigOfflineWindow:
                _rigs.TryRemove(key, out _); // respawn -> back to untracked Online
                recordInto?.Add(new RigCrossing(key.Guild, key.Server, key.Rig, RigEventKind.Respawned,
                    entry.X, entry.Y, entry.Dimensions));
                next = null;
                return true;
            default:
                next = entry;
                return false;
        }
    }

    private TimeSpan? Remaining(Entry entry, DateTimeOffset now)
    {
        var opts = options.Value;
        var window = entry.Status switch
        {
            RigStatus.Active => opts.RigActiveWindow,
            RigStatus.Offline => opts.RigOfflineWindow,
            _ => (TimeSpan?)null,
        };
        if (window is not { } w)
        {
            return null;
        }

        var remaining = w - (now - entry.PhaseStart);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private sealed record Entry(
        RigStatus Status,
        DateTimeOffset PhaseStart,
        float X,
        float Y,
        MapDimensions? Dimensions);
}
