using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Diffs successive team snapshots into presence transitions. One instance per connected window.</summary>
/// <remarks>Not thread-safe: the supervisor calls <see cref="Diff"/> from a single poll loop.</remarks>
internal sealed class TeamStateTracker
{
    private Dictionary<ulong, TeamMemberSnapshot>? _baseline;

    /// <summary>Diffs <paramref name="snapshot"/> against the previous one. First non-null call primes silently.</summary>
    /// <param name="snapshot">The latest team snapshot, or null when the poll returned no data.</param>
    /// <returns>The transitions since the previous snapshot; empty on prime, null input, or no change.</returns>
    public IReadOnlyList<PlayerTransition> Diff(TeamInfoSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [];
        }

        var current = snapshot.Members.ToDictionary(m => m.SteamId);

        if (_baseline is null)
        {
            _baseline = current; // first poll: silent baseline
            return [];
        }

        var transitions = new List<PlayerTransition>();
        foreach (var (id, now) in current)
        {
            if (!_baseline.TryGetValue(id, out var was))
            {
                continue; // brand-new member: prime silently this poll
            }

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
                transitions.Add(new PlayerTransition(
                    PlayerTransitionKind.Respawn, id, now.Name, (now.X, now.Y)));
            }
        }

        _baseline = current;
        return transitions;
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
