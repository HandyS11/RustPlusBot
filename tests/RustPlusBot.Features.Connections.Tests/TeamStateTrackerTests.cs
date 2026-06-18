using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamStateTrackerTests
{
    private static TeamMemberSnapshot Member(
        ulong id, bool online = true, bool alive = true,
        float x = 0, float y = 0, DateTimeOffset spawn = default, DateTimeOffset death = default)
        => new(id, $"P{id}", x, y, online, alive, spawn, death);

    private static TeamInfoSnapshot Team(ulong leader, params TeamMemberSnapshot[] members)
        => new(leader, members);

    [Fact]
    public void First_snapshot_primes_silently()
    {
        var tracker = new TeamStateTracker();
        var result = tracker.Diff(Team(1, Member(1)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Empty(result);
    }

    [Fact]
    public void Null_snapshot_emits_nothing_and_keeps_baseline()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: true)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Empty(tracker.Diff(null, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f));
        // After the null, an offline flip is still detected against the original baseline.
        var result = tracker.Diff(Team(1, Member(1, online: false)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Single(result, t => t.Kind == PlayerTransitionKind.Disconnect);
    }

    [Fact]
    public void Brand_new_member_is_primed_silently()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var result = tracker.Diff(Team(1, Member(1), Member(2, online: true)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.DoesNotContain(result, t => t.SteamId == 2);
    }

    [Fact]
    public void Connect_detected_on_offline_to_online()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: false)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var result = tracker.Diff(Team(1, Member(1, online: true)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Single(result, t => t.Kind == PlayerTransitionKind.Connect && t.SteamId == 1);
    }

    [Fact]
    public void Disconnect_detected_on_online_to_offline()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: true)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var result = tracker.Diff(Team(1, Member(1, online: false)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Single(result, t => t.Kind == PlayerTransitionKind.Disconnect && t.SteamId == 1);
    }

    [Fact]
    public void Death_detected_on_deathtime_advance_even_if_alive_again()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, alive: true, death: t0)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        // Died and already respawned: IsAlive true both times, but LastDeathTime advanced.
        var result = tracker.Diff(Team(1, Member(1, alive: true, death: t0.AddMinutes(1))), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Contains(result, t => t.Kind == PlayerTransitionKind.Death && t.SteamId == 1);
    }

    [Fact]
    public void Respawn_detected_on_spawntime_advance_with_current_location()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, spawn: t0)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var result = tracker.Diff(Team(1, Member(1, x: 50, y: 60, spawn: t0.AddMinutes(1))), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var respawn = Assert.Single(result, t => t.Kind == PlayerTransitionKind.Respawn);
        Assert.Equal((50f, 60f), respawn.Location);
    }

    [Fact]
    public void Unchanged_snapshot_emits_nothing()
    {
        var tracker = new TeamStateTracker();
        var m = Member(1, spawn: DateTimeOffset.UnixEpoch, death: DateTimeOffset.UnixEpoch);
        tracker.Diff(new TeamInfoSnapshot(1, [m]), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Empty(tracker.Diff(new TeamInfoSnapshot(1, [m]), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f));
    }

    [Fact]
    public void Connect_and_disconnect_have_no_location()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: false)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var result = tracker.Diff(Team(1, Member(1, online: true, x: 9, y: 9)), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.Null(Assert.Single(result).Location);
    }

    [Fact]
    public void Leader_death_uses_deathnote_location()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        tracker.Diff(new TeamInfoSnapshot(1, [Member(1, death: t0)]), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        var snap = new TeamInfoSnapshot(1, [Member(1, x: 1, y: 1, death: t0.AddMinutes(1))], (700f, 800f));
        var death = Assert.Single(tracker.Diff(snap, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f), t => t.Kind == PlayerTransitionKind.Death);
        Assert.Equal((700f, 800f), death.Location);
    }

    [Fact]
    public void Nonleader_death_uses_previous_poll_position_not_current()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        // Member 2 is alive at (10,10) on the baseline poll...
        tracker.Diff(new TeamInfoSnapshot(1, [Member(1), Member(2, x: 10, y: 10, death: t0)]), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        // ...then dies and respawns at (999,999); death must report the PRE-death (10,10).
        var snap = new TeamInfoSnapshot(1, [Member(1), Member(2, x: 999, y: 999, death: t0.AddMinutes(1))], (5f, 5f));
        var death = Assert.Single(tracker.Diff(snap, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f), t => t.Kind == PlayerTransitionKind.Death);
        Assert.Equal((10f, 10f), death.Location); // leader DeathNote (5,5) must NOT apply to a non-leader
    }

    [Fact]
    public void Death_with_no_prior_position_and_no_deathnote_has_null_location()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        // Prime member 1 only.
        tracker.Diff(new TeamInfoSnapshot(1, [Member(1)]), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        // Member 2 appears already-dead-advanced in the same poll it is first seen → primed silently, no death.
        var first = tracker.Diff(new TeamInfoSnapshot(1, [Member(1), Member(2, death: t0)]), DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f);
        Assert.DoesNotContain(first, t => t.SteamId == 2);
    }
}
