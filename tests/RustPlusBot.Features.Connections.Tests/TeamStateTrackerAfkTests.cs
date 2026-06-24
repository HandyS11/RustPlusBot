using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamStateTrackerAfkTests
{
    private const float Eps = 1f;
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    private static TeamMemberSnapshot Member(
        ulong id,
        float x,
        float y,
        bool online = true,
        bool alive = true,
        DateTimeOffset death = default)
        => new(id, $"P{id}", x, y, online, alive, DateTimeOffset.UnixEpoch, death);

    private static TeamInfoSnapshot Team(params TeamMemberSnapshot[] m) => new(1, m);

    [Fact]
    public void Still_for_threshold_emits_one_BecameAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps); // prime
        Assert.Empty(t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(2), Threshold, Eps)); // still, < threshold
        var afk = t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps); // crossed
        Assert.Single(afk, x => x.Kind == PlayerTransitionKind.BecameAfk && x.Location == (0f, 0f));
        Assert.Empty(t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(9), Threshold, Eps)); // latched, no repeat
    }

    [Fact]
    public void Moving_after_afk_emits_ReturnedFromAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps); // BecameAfk
        var back = t.Diff(Team(Member(1, 50, 50)), t0.AddMinutes(7), Threshold, Eps);
        Assert.Single(back, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk && x.Location == null);
    }

    [Fact]
    public void Going_offline_while_afk_clears_silently_without_ReturnedFromAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps); // BecameAfk
        var off = t.Diff(Team(Member(1, 0, 0, online: false)), t0.AddMinutes(7), Threshold, Eps);
        // The Disconnect transition speaks for them; no contradictory "is back" line.
        Assert.DoesNotContain(off, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk);
        Assert.Contains(off, x => x.Kind == PlayerTransitionKind.Disconnect);
        Assert.Empty(t.CurrentAfk(t0.AddMinutes(7)));
    }

    [Fact]
    public void Dying_while_afk_clears_silently_even_if_already_respawned()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps); // BecameAfk
        // Slow poll: died and already respawned (IsAlive true), but LastDeathTime advanced.
        var died = t.Diff(
            Team(Member(1, 0, 0, alive: true, death: t0.AddMinutes(7))), t0.AddMinutes(8), Threshold, Eps);
        Assert.DoesNotContain(died, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk);
        Assert.Contains(died, x => x.Kind == PlayerTransitionKind.Death);
        Assert.Empty(t.CurrentAfk(t0.AddMinutes(8)));
    }

    [Fact]
    public void Dead_member_is_never_afk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0, alive: false)), t0, Threshold, Eps);
        Assert.Empty(t.Diff(Team(Member(1, 0, 0, alive: false)), t0.AddMinutes(10), Threshold, Eps));
    }

    [Fact]
    public void Small_jitter_below_epsilon_still_counts_as_still()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        var afk = t.Diff(Team(Member(1, 0.5f, 0.5f)), t0.AddMinutes(6), Threshold, Eps); // < 1 unit move
        Assert.Single(afk, x => x.Kind == PlayerTransitionKind.BecameAfk);
    }

    [Fact]
    public void Diagonal_move_beyond_epsilon_radius_counts_as_moved()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        // dx=dy=0.8 → distance ≈ 1.13 > epsilon 1; per-axis would wrongly call this "still".
        var afk = t.Diff(Team(Member(1, 0.8f, 0.8f)), t0.AddMinutes(6), Threshold, Eps);
        Assert.DoesNotContain(afk, x => x.Kind == PlayerTransitionKind.BecameAfk);
    }

    [Fact]
    public void Departed_member_state_is_pruned()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0), Member(2, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0), Member(2, 0, 0)), t0.AddMinutes(6), Threshold, Eps); // both BecameAfk
        Assert.Equal(2, t.CurrentAfk(t0.AddMinutes(6)).Count);
        // Member 2 leaves the team (absent from the snapshot entirely).
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(7), Threshold, Eps);
        var afk = t.CurrentAfk(t0.AddMinutes(7));
        Assert.Single(afk);
        Assert.Equal(1UL, afk[0].SteamId);
    }
}
