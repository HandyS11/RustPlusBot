using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamStateTrackerAfkTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);
    private const float Eps = 1f;

    private static TeamMemberSnapshot Member(ulong id, float x, float y, bool online = true, bool alive = true)
        => new(id, $"P{id}", x, y, online, alive, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TeamInfoSnapshot Team(params TeamMemberSnapshot[] m) => new(1, m);

    [Fact]
    public void Still_for_threshold_emits_one_BecameAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);                       // prime
        Assert.Empty(t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(2), Threshold, Eps)); // still, < threshold
        var afk = t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps);     // crossed
        Assert.Single(afk, x => x.Kind == PlayerTransitionKind.BecameAfk && x.Location == (0f, 0f));
        Assert.Empty(t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(9), Threshold, Eps)); // latched, no repeat
    }

    [Fact]
    public void Moving_after_afk_emits_ReturnedFromAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps);          // BecameAfk
        var back = t.Diff(Team(Member(1, 50, 50)), t0.AddMinutes(7), Threshold, Eps);
        Assert.Single(back, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk && x.Location == null);
    }

    [Fact]
    public void Going_offline_while_afk_emits_ReturnedFromAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps);          // BecameAfk
        var off = t.Diff(Team(Member(1, 0, 0, online: false)), t0.AddMinutes(7), Threshold, Eps);
        Assert.Contains(off, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk);
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
}
