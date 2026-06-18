using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Diffs successive team snapshots into presence transitions. One instance per connected window.</summary>
internal sealed class TeamStateTracker
{
    private readonly object _gate = new();
    private Dictionary<ulong, TeamMemberSnapshot>? _baseline;
    private readonly Dictionary<ulong, DateTimeOffset> _stillSince = new();
    private readonly HashSet<ulong> _afk = new();

    /// <summary>Diffs <paramref name="snapshot"/> against the previous one. First non-null call primes silently.</summary>
    /// <param name="snapshot">The latest team snapshot, or null when the poll returned no data.</param>
    /// <param name="now">Current wall-clock time (from <c>IClock.UtcNow</c>).</param>
    /// <param name="afkThreshold">How long a member must be still before being flagged AFK.</param>
    /// <param name="afkEpsilon">Movement tolerance (world units) below which a member is considered still.</param>
    /// <returns>The transitions since the previous snapshot; empty on prime, null input, or no change.</returns>
    public IReadOnlyList<PlayerTransition> Diff(
        TeamInfoSnapshot? snapshot, DateTimeOffset now, TimeSpan afkThreshold, float afkEpsilon)
    {
        if (snapshot is null)
        {
            return [];
        }

        var current = snapshot.Members.ToDictionary(m => m.SteamId);

        lock (_gate)
        {
            if (_baseline is null)
            {
                _baseline = current;
                foreach (var m in current.Values)
                {
                    _stillSince[m.SteamId] = now;
                }

                return [];
            }

            var transitions = new List<PlayerTransition>();
            foreach (var (id, nowMember) in current)
            {
                if (!_baseline.TryGetValue(id, out var was))
                {
                    _stillSince[id] = now; // prime new member's stillness clock
                    continue;
                }

                AddPresenceTransitions(transitions, id, was, nowMember, snapshot);
                UpdateAfk(transitions, id, was, nowMember, now, afkThreshold, afkEpsilon);
            }

            _baseline = current;
            return transitions;
        }
    }

    private static void AddPresenceTransitions(
        List<PlayerTransition> transitions, ulong id,
        TeamMemberSnapshot was, TeamMemberSnapshot now, TeamInfoSnapshot snapshot)
    {
        if (now.IsOnline && !was.IsOnline)
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.Connect, id, now.Name, null));
        }
        else if (!now.IsOnline && was.IsOnline)
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.Disconnect, id, now.Name, null));
        }

        if (now.LastDeathTimeUtc > was.LastDeathTimeUtc)
        {
            transitions.Add(new PlayerTransition(
                PlayerTransitionKind.Death, id, now.Name, ResolveDeathLocation(id, snapshot, was)));
        }

        if (now.LastSpawnTimeUtc > was.LastSpawnTimeUtc)
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.Respawn, id, now.Name, (now.X, now.Y)));
        }
    }

    private void UpdateAfk(
        List<PlayerTransition> transitions, ulong id,
        TeamMemberSnapshot was, TeamMemberSnapshot now, DateTimeOffset clock, TimeSpan threshold, float epsilon)
    {
        var eligible = now.IsOnline && now.IsAlive;
        if (!eligible)
        {
            if (_afk.Remove(id))
            {
                transitions.Add(new PlayerTransition(PlayerTransitionKind.ReturnedFromAfk, id, now.Name, null));
            }

            _stillSince[id] = clock;
            return;
        }

        var moved = Math.Abs(now.X - was.X) > epsilon || Math.Abs(now.Y - was.Y) > epsilon;
        if (moved)
        {
            _stillSince[id] = clock;
            if (_afk.Remove(id))
            {
                transitions.Add(new PlayerTransition(PlayerTransitionKind.ReturnedFromAfk, id, now.Name, null));
            }

            return;
        }

        var since = _stillSince.TryGetValue(id, out var s) ? s : clock;
        _stillSince.TryAdd(id, since);
        if (clock - since >= threshold && _afk.Add(id))
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.BecameAfk, id, now.Name, (now.X, now.Y)));
        }
    }

    /// <summary>The members currently flagged AFK and how long each has been still, as of <paramref name="now"/>.</summary>
    /// <param name="now">The current wall-clock time used to compute each member's still duration.</param>
    public IReadOnlyList<AfkMember> CurrentAfk(DateTimeOffset now)
    {
        lock (_gate)
        {
            var result = new List<AfkMember>();
            foreach (var id in _afk)
            {
                if (_baseline is not null && _baseline.TryGetValue(id, out var m))
                {
                    var since = _stillSince.TryGetValue(id, out var s) ? s : now;
                    result.Add(new AfkMember(id, m.Name, now - since));
                }
            }

            return result;
        }
    }

    private static (float X, float Y)? ResolveDeathLocation(
        ulong steamId, TeamInfoSnapshot snapshot, TeamMemberSnapshot previous)
    {
        // Leader: the single DeathNote is the true death spot (player respawns elsewhere).
        if (steamId == snapshot.LeaderSteamId && snapshot.DeathNote is { } note)
        {
            return note;
        }

        // Everyone else: the previous poll's position (captured while alive) ≈ where they died.
        return (previous.X, previous.Y);
    }
}
