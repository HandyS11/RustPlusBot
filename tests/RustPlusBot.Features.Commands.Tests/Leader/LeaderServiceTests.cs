using NSubstitute;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Leader;

public sealed class LeaderServiceTests
{
    private static readonly CommandLocalizer Loc = new(CommandLocalizationCatalog.Default);
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public async Task GetMembers_ReturnsError_WhenNotConnected()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1UL, Server, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);
        var service = new LeaderService(query, Loc);

        var result = await service.GetMembersAsync(1UL, Server, "en", CancellationToken.None);

        Assert.Empty(result.Members);
        Assert.Equal("Not connected to this server.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetMembers_ReturnsError_WhenTeamEmpty()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1UL, Server, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(0UL, []));
        var service = new LeaderService(query, Loc);

        var result = await service.GetMembersAsync(1UL, Server, "en", CancellationToken.None);

        Assert.Empty(result.Members);
        Assert.Equal("No team members to promote.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetMembers_MapsMembersAndMarksLeader()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1UL, Server, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(
                10UL,
                [
                    new TeamMemberSnapshot(10UL, "alice", 0f, 0f, true, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                    new TeamMemberSnapshot(20UL, "bob", 0f, 0f, true, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                ]));
        var service = new LeaderService(query, Loc);

        var result = await service.GetMembersAsync(1UL, Server, "en", CancellationToken.None);

        Assert.Null(result.ErrorMessage);
        Assert.Equal(2, result.Members.Count);
        Assert.Contains(result.Members, m => m is { SteamId: 10UL, Name: "alice", IsLeader: true });
        Assert.Contains(result.Members, m => m is { SteamId: 20UL, Name: "bob", IsLeader: false });
    }

    [Fact]
    public async Task Promote_ReturnsSuccessMessage_WhenPromoted()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.PromoteToLeaderAsync(1UL, Server, 20UL, Arg.Any<CancellationToken>()).Returns(true);
        var service = new LeaderService(query, Loc);

        var message = await service.PromoteAsync(1UL, Server, 20UL, "bob", "en", CancellationToken.None);

        Assert.Equal("Promoted bob to team leader.", message);
    }

    [Fact]
    public async Task Promote_ReturnsFailureMessage_WhenApiFails()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.PromoteToLeaderAsync(1UL, Server, 20UL, Arg.Any<CancellationToken>()).Returns(false);
        var service = new LeaderService(query, Loc);

        var message = await service.PromoteAsync(1UL, Server, 20UL, "bob", "en", CancellationToken.None);

        Assert.Equal("Couldn't promote — the server may be unreachable.", message);
    }
}
