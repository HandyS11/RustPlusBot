using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class AfkStateTests
{
    [Fact]
    public void CurrentAfk_lists_member_with_still_duration()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        var m = new TeamMemberSnapshot(1, "Bob", 0, 0, true, true, t0, t0);
        t.Diff(new TeamInfoSnapshot(1, [m]), t0, TimeSpan.FromMinutes(5), 1f);
        t.Diff(new TeamInfoSnapshot(1, [m]), t0.AddMinutes(6), TimeSpan.FromMinutes(5), 1f); // BecameAfk

        var afk = t.CurrentAfk(t0.AddMinutes(6));
        var bob = Assert.Single(afk);
        Assert.Equal(1UL, bob.SteamId);
        Assert.Equal(TimeSpan.FromMinutes(6), bob.StillFor);
    }

    [Fact]
    public void CurrentAfk_empty_when_nobody_afk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        var m = new TeamMemberSnapshot(1, "Bob", 0, 0, true, true, t0, t0);
        t.Diff(new TeamInfoSnapshot(1, [m]), t0, TimeSpan.FromMinutes(5), 1f);
        Assert.Empty(t.CurrentAfk(t0.AddMinutes(1)));
    }
}
