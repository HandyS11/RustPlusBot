using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Clans.Writing;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Tests.Writing;

public sealed class ClanMotdWriterTests
{
    private const ulong Guild = 42UL;
    private const ulong Actor = 7UL;

    private static readonly Guid Server = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Rejects_a_writer_with_no_stored_clan()
    {
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((ClanSnapshot?)null);
        var query = Substitute.For<IRustServerQuery>();
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "New MOTD", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.NotPermitted, result);
        await query.DidNotReceive()
            .SetClanMotdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_an_actor_who_is_not_a_clan_member()
    {
        var clan = Clan(members: [Member(1UL, 1)]);
        var store = Store(clan);
        var query = Substitute.For<IRustServerQuery>();
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "New MOTD", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.NotPermitted, result);
        await query.DidNotReceive()
            .SetClanMotdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_an_actor_whose_role_cannot_set_the_motd()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Member", canSetMotd: false)],
            members: [Member(Actor, 1)]);
        var store = Store(clan);
        var query = Substitute.For<IRustServerQuery>();
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "New MOTD", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.NotPermitted, result);
        await query.DidNotReceive()
            .SetClanMotdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_an_actor_whose_role_id_is_unknown()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(Actor, 99)]);
        var store = Store(clan);
        var query = Substitute.For<IRustServerQuery>();
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "New MOTD", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.NotPermitted, result);
        await query.DidNotReceive()
            .SetClanMotdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Writes_the_motd_when_the_actor_is_permitted()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(Actor, 1)]);
        var store = Store(clan);
        var query = Substitute.For<IRustServerQuery>();
        query.SetClanMotdAsync(Guild, Server, "New MOTD", Arg.Any<CancellationToken>()).Returns(true);
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "New MOTD", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.Ok, result);
        await query.Received(1).SetClanMotdAsync(Guild, Server, "New MOTD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_Failed_when_the_socket_write_fails()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(Actor, 1)]);
        var store = Store(clan);
        var query = Substitute.For<IRustServerQuery>();
        query.SetClanMotdAsync(Guild, Server, "New MOTD", Arg.Any<CancellationToken>()).Returns(false);
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "New MOTD", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.Failed, result);
    }

    [Fact]
    public async Task Trims_and_rejects_a_blank_motd()
    {
        var clan = Clan(
            roles: [Role(1, 0, "Leader", canSetMotd: true)],
            members: [Member(Actor, 1)]);
        var store = Store(clan);
        var query = Substitute.For<IRustServerQuery>();
        var writer = new ClanMotdWriter(store, query);

        var result = await writer.SetAsync(Guild, Server, Actor, "   ", CancellationToken.None);

        Assert.Equal(ClanMotdWriteResult.Failed, result);
        await query.DidNotReceive()
            .SetClanMotdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IClanStore Store(ClanSnapshot clan)
    {
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(clan);
        return store;
    }

    private static ClanRoleSnapshot Role(int roleId, int rank, string name, bool canSetMotd = false) =>
        new(roleId, rank, name, canSetMotd, false, false, false, false, false, false, false, false);

    private static ClanMemberSnapshot Member(ulong steamId, int roleId) =>
        new(steamId, roleId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, true);

    private static ClanSnapshot Clan(
        IReadOnlyList<ClanRoleSnapshot>? roles = null,
        IReadOnlyList<ClanMemberSnapshot>? members = null) => new(
        1,
        "Wolves",
        DateTimeOffset.UnixEpoch,
        1UL,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        roles ?? [Role(1, 0, "Leader")],
        members ?? [Member(1UL, 1)],
        []);
}
