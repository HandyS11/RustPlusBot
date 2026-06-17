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

#pragma warning disable S1144, CA1812 // Shared test helper: instantiated by Tasks 9–11 facts appended to this class.
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }
#pragma warning restore S1144, CA1812

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
}
