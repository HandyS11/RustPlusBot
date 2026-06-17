using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class TeamIntelHandlersTests
{
    private static readonly ICommandLocalizer Loc = new CommandLocalizer(CommandLocalizationCatalog.Default);

    private static CommandContext Ctx(params string[] args) =>
        new(1, ServerId, "en", 7UL, "alice", args); // Caller alice = steamId 7, present in every snapshot at (0,0).
    private static readonly Guid ServerId = Guid.NewGuid();

    private static TeamMemberSnapshot Member(
        ulong id, string name, bool online = true, bool alive = true,
        float x = 0f, float y = 0f, DateTimeOffset? spawn = null) =>
        new(id, name, x, y, online, alive, spawn ?? DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static IRustServerQuery QueryReturning(TeamInfoSnapshot? snapshot)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(snapshot);
        return query;
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    [Fact]
    public async Task Online_ListsOnlineMembers()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", online: true),
            Member(8UL, "bob", online: true),
            Member(9UL, "carl", online: false),
        ]));
        var reply = await new OnlineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Online (2): alice, bob", reply);
    }

    [Fact]
    public async Task Online_None_WhenNobodyOnline()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL, [Member(8UL, "bob", online: false)]));
        var reply = await new OnlineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No one is online.", reply);
    }

    [Fact]
    public async Task Online_NotConnected_WhenNull()
    {
        var reply = await new OnlineCommandHandler(QueryReturning(null), Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Not connected to the server.", reply);
    }

    [Fact]
    public async Task Offline_ListsOfflineMembers()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", online: true),
            Member(9UL, "carl", online: false),
        ]));
        var reply = await new OfflineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Offline (1): carl", reply);
    }

    [Fact]
    public async Task Offline_None_WhenEveryoneOnline()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL, [Member(7UL, "alice", online: true)]));
        var reply = await new OfflineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Everyone is online.", reply);
    }

    [Fact]
    public async Task Team_ListsAllMembers()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice"),
            Member(8UL, "bob", online: false),
        ]));
        var reply = await new TeamCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Team (2): alice, bob", reply);
    }

    [Fact]
    public async Task Team_None_WhenNoMembers()
    {
        var reply = await new TeamCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc)
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }

    [Fact]
    public async Task SteamId_NoArg_ListsAll()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice"),
            Member(8UL, "bob"),
        ]));
        var reply = await new SteamIdCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("alice 7, bob 8", reply);
    }

    [Fact]
    public async Task SteamId_NameArg_FiltersToOne()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice"),
            Member(8UL, "bob"),
        ]));
        var reply = await new SteamIdCommandHandler(query, Loc).ExecuteAsync(Ctx("bob"), CancellationToken.None);
        Assert.Equal("bob 8", reply);
    }

    [Fact]
    public async Task SteamId_NameArg_NoMatch_ReportsNoMatch()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL, [Member(7UL, "alice")]));
        var reply = await new SteamIdCommandHandler(query, Loc).ExecuteAsync(Ctx("zed"), CancellationToken.None);
        Assert.Equal("No teammate matches 'zed'.", reply);
    }

    [Fact]
    public async Task SteamId_None_WhenNoMembers()
    {
        var reply = await new SteamIdCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc)
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }

    [Fact]
    public async Task Alive_SortsBySurvivalDesc_ThenDeadLast()
    {
        var clock = new TestClock { UtcNow = DateTimeOffset.UnixEpoch.AddHours(3) };
        // carl spawned at epoch -> 3h survival; alice spawned at +2h -> 1h survival; bob dead.
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", alive: true, spawn: DateTimeOffset.UnixEpoch.AddHours(2)),
            Member(8UL, "bob", alive: false),
            Member(9UL, "carl", alive: true, spawn: DateTimeOffset.UnixEpoch),
        ]));
        var reply = await new AliveCommandHandler(query, Loc, clock).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Alive: carl 3h 0m, alice 1h 0m, bob dead", reply);
    }

    [Fact]
    public async Task Alive_None_WhenNoMembers()
    {
        var reply = await new AliveCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc, new TestClock())
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }

    [Fact]
    public async Task Alive_NotConnected_WhenNull()
    {
        var reply = await new AliveCommandHandler(QueryReturning(null), Loc, new TestClock())
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Not connected to the server.", reply);
    }
}
