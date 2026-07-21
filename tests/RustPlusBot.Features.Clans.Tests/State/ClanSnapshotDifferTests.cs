using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Clans.State;

namespace RustPlusBot.Features.Clans.Tests.State;

public sealed class ClanSnapshotDifferTests
{
    private static ClanSnapshot Clan(
        long clanId = 1,
        string name = "Wolves",
        string? motd = null,
        ulong? motdAuthor = null,
        string? logoHash = null,
        int? color = null,
        long? score = null,
        IReadOnlyList<ClanRoleSnapshot>? roles = null,
        IReadOnlyList<ClanMemberSnapshot>? members = null,
        IReadOnlyList<ClanInviteSnapshot>? invites = null) => new(
            clanId,
            name,
            DateTimeOffset.UnixEpoch,
            1,
            motd,
            motd is null ? null : DateTimeOffset.UnixEpoch,
            motdAuthor,
            logoHash,
            color,
            null,
            score,
            roles ?? [Role(1, 0, "Member")],
            members ?? [],
            invites ?? []);

    private static ClanRoleSnapshot Role(int roleId, int rank, string name) =>
        new(roleId, rank, name, false, false, false, false, false, false, false, false, false);

    private static ClanMemberSnapshot Member(ulong steamId, int roleId = 1, bool online = false) =>
        new(steamId, roleId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, online);

    private static ClanInviteSnapshot Invite(ulong steamId, ulong recruiter = 99) =>
        new(steamId, recruiter, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Emits_nothing_for_a_first_snapshot()
    {
        var current = Clan(members: [Member(10), Member(20)], invites: [Invite(30)]);

        var result = ClanSnapshotDiffer.Diff(null, current);

        Assert.Empty(result);
    }

    [Fact]
    public void Emits_nothing_when_both_snapshots_are_null()
    {
        var result = ClanSnapshotDiffer.Diff(null, null);

        Assert.Empty(result);
    }

    [Fact]
    public void Emits_nothing_when_nothing_changed()
    {
        var previous = Clan(score: 100, members: [Member(10)], invites: [Invite(20)]);
        var current = Clan(score: 100, members: [Member(10)], invites: [Invite(20)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.Empty(result);
    }

    [Fact]
    public void Emits_dissolved_when_the_clan_goes_away()
    {
        var previous = Clan(name: "Wolves");

        var result = ClanSnapshotDiffer.Diff(previous, null);

        var change = Assert.Single(result);
        Assert.Equal(ClanChangeKind.Dissolved, change.Kind);
        Assert.Equal("Wolves", change.Text);
    }

    [Fact]
    public void Emits_nothing_when_the_player_joined_a_different_clan()
    {
        var previous = Clan(clanId: 1, members: [Member(10)]);
        var current = Clan(clanId: 2, members: [Member(20)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.Empty(result);
    }

    [Fact]
    public void Detects_a_rename()
    {
        var previous = Clan(name: "Wolves");
        var current = Clan(name: "Dire Wolves");

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.Renamed, change.Kind);
        Assert.Equal("Dire Wolves", change.Text);
    }

    [Fact]
    public void Detects_a_motd_change_and_carries_the_author()
    {
        var previous = Clan(motd: null);
        var current = Clan(motd: "gg all", motdAuthor: 42);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.MotdChanged, change.Kind);
        Assert.Equal("gg all", change.Text);
        Assert.Equal((ulong?)42, change.ActorSteamId);
    }

    [Fact]
    public void Detects_a_member_joining()
    {
        var previous = Clan(members: []);
        var current = Clan(members: [Member(10)]);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.MemberJoined, change.Kind);
        Assert.Equal((ulong?)10, change.SteamId);
    }

    [Fact]
    public void Detects_a_member_leaving()
    {
        var previous = Clan(members: [Member(10)]);
        var current = Clan(members: []);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.MemberLeft, change.Kind);
        Assert.Equal((ulong?)10, change.SteamId);
    }

    [Fact]
    public void Orders_joined_members_ascending_by_steam_id_regardless_of_input_order()
    {
        var previous = Clan(members: []);
        var current = Clan(members: [Member(50), Member(10), Member(30)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.All(result, c => Assert.Equal(ClanChangeKind.MemberJoined, c.Kind));
        ulong?[] expected = [10, 30, 50];
        Assert.Equal(expected, [.. result.Select(c => c.SteamId)]);
    }

    [Fact]
    public void Orders_left_members_ascending_by_steam_id_regardless_of_input_order()
    {
        var previous = Clan(members: [Member(50), Member(10), Member(30)]);
        var current = Clan(members: []);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.All(result, c => Assert.Equal(ClanChangeKind.MemberLeft, c.Kind));
        ulong?[] expected = [10, 30, 50];
        Assert.Equal(expected, [.. result.Select(c => c.SteamId)]);
    }

    [Fact]
    public void Detects_a_promotion_using_rank_order()
    {
        IReadOnlyList<ClanRoleSnapshot> roles = [Role(1, 2, "Member"), Role(2, 0, "Leader")];
        var previous = Clan(roles: roles, members: [Member(10, roleId: 1)]);
        var current = Clan(roles: roles, members: [Member(10, roleId: 2)]);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.MemberPromoted, change.Kind);
        Assert.Equal((ulong?)10, change.SteamId);
        Assert.Equal("Leader", change.RoleName);
    }

    [Fact]
    public void Detects_a_demotion_using_rank_order()
    {
        IReadOnlyList<ClanRoleSnapshot> roles = [Role(1, 0, "Leader"), Role(2, 2, "Member")];
        var previous = Clan(roles: roles, members: [Member(10, roleId: 1)]);
        var current = Clan(roles: roles, members: [Member(10, roleId: 2)]);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.MemberDemoted, change.Kind);
        Assert.Equal((ulong?)10, change.SteamId);
        Assert.Equal("Member", change.RoleName);
    }

    [Fact]
    public void Emits_no_role_change_when_the_new_role_is_unknown()
    {
        IReadOnlyList<ClanRoleSnapshot> roles = [Role(1, 0, "Leader")];
        var previous = Clan(roles: roles, members: [Member(10, roleId: 1)]);
        var current = Clan(roles: roles, members: [Member(10, roleId: 2)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.Empty(result);
    }

    [Fact]
    public void Emits_no_role_change_when_the_old_role_is_unknown()
    {
        // The member's previous role id (1) does not exist in current.Roles, while the new role
        // id (2) does. The direction can't be judged without both endpoints, so nothing is emitted.
        var previous = Clan(members: [Member(10, roleId: 1)]);
        var current = Clan(roles: [Role(2, 0, "Leader")], members: [Member(10, roleId: 2)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.Empty(result);
    }

    [Fact]
    public void Detects_an_invite_being_sent()
    {
        var previous = Clan(invites: []);
        var current = Clan(invites: [Invite(30, recruiter: 5)]);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.InviteSent, change.Kind);
        Assert.Equal((ulong?)30, change.SteamId);
        Assert.Equal((ulong?)5, change.ActorSteamId);
    }

    [Fact]
    public void Orders_sent_invites_ascending_by_steam_id_regardless_of_input_order()
    {
        var previous = Clan(invites: []);
        var current = Clan(invites: [Invite(50), Invite(10), Invite(30)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        Assert.All(result, c => Assert.Equal(ClanChangeKind.InviteSent, c.Kind));
        ulong?[] expected = [10, 30, 50];
        Assert.Equal(expected, [.. result.Select(c => c.SteamId)]);
    }

    [Fact]
    public void Detects_an_invite_being_revoked()
    {
        var previous = Clan(invites: [Invite(30)]);
        var current = Clan(invites: []);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.InviteRevoked, change.Kind);
        Assert.Equal((ulong?)30, change.SteamId);
    }

    [Fact]
    public void Reports_an_accepted_invite_once_and_not_as_a_join()
    {
        var previous = Clan(members: [], invites: [Invite(7)]);
        var current = Clan(members: [Member(7)], invites: []);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.InviteAccepted, change.Kind);
        Assert.Equal((ulong?)7, change.SteamId);
    }

    [Fact]
    public void Detects_a_logo_change()
    {
        var previous = Clan(logoHash: "abc123");
        var current = Clan(logoHash: "def456");

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.LogoChanged, change.Kind);
    }

    [Fact]
    public void Detects_a_colour_change()
    {
        var previous = Clan(color: 0xFF0000);
        var current = Clan(color: 0x00FF00);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.ColorChanged, change.Kind);
    }

    [Fact]
    public void Detects_a_score_change_and_carries_the_new_score()
    {
        var previous = Clan(score: 100);
        var current = Clan(score: 250);

        var change = Assert.Single(ClanSnapshotDiffer.Diff(previous, current));

        Assert.Equal(ClanChangeKind.ScoreChanged, change.Kind);
        Assert.Equal((long?)250, change.Score);
    }

    [Fact]
    public void Orders_changes_deterministically()
    {
        // Changes name, motd, members (a join, a leave and a promotion), invites (a send, an
        // acceptance and a revocation), logo, colour and score all at once, and asserts that the
        // emitted Kind sequence matches the documented order exactly.
        IReadOnlyList<ClanRoleSnapshot> roles = [Role(1, 2, "Member"), Role(2, 0, "Leader")];
        var previous = Clan(
            name: "Wolves",
            motd: "old motd",
            motdAuthor: 1,
            logoHash: "logo1",
            color: 100,
            score: 500,
            roles: roles,
            members: [Member(1, roleId: 1), Member(2, roleId: 1), Member(3, roleId: 1)],
            invites: [Invite(50), Invite(60)]);
        var current = Clan(
            name: "Dire Wolves",
            motd: "new motd",
            motdAuthor: 2,
            logoHash: "logo2",
            color: 200,
            score: 999,
            roles: roles,
            members: [Member(2, roleId: 2), Member(3, roleId: 1), Member(4, roleId: 1), Member(60, roleId: 1)],
            invites: [Invite(70)]);

        var result = ClanSnapshotDiffer.Diff(previous, current);

        var kinds = result.Select(c => c.Kind).ToArray();
        ClanChangeKind[] expected =
        [
            ClanChangeKind.Renamed,
            ClanChangeKind.MotdChanged,
            ClanChangeKind.MemberJoined,
            ClanChangeKind.MemberLeft,
            ClanChangeKind.MemberPromoted,
            ClanChangeKind.InviteSent,
            ClanChangeKind.InviteAccepted,
            ClanChangeKind.InviteRevoked,
            ClanChangeKind.LogoChanged,
            ClanChangeKind.ColorChanged,
            ClanChangeKind.ScoreChanged,
        ];
        Assert.Equal(expected, kinds);
    }
}
