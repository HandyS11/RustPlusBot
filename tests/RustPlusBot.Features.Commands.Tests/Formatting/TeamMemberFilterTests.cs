using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class TeamMemberFilterTests
{
    private static readonly IReadOnlyList<TeamMemberSnapshot> Members =
    [
        Member(1UL, "Alice"),
        Member(2UL, "Bob"),
        Member(3UL, "Bobby"),
    ];

    private static TeamMemberSnapshot Member(ulong id, string name) =>
        new(id, name, 0f, 0f, true, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ByName_NullArg_ReturnsAll()
    {
        Assert.Equal(3, TeamMemberFilter.ByName(Members, null).Count);
    }

    [Fact]
    public void ByName_EmptyArg_ReturnsAll()
    {
        Assert.Equal(3, TeamMemberFilter.ByName(Members, "  ").Count);
    }

    [Fact]
    public void ByName_PartialCaseInsensitive_MatchesSubstring()
    {
        var result = TeamMemberFilter.ByName(Members, "bob");
        Assert.Equal(2, result.Count); // Bob + Bobby
        Assert.Contains(result, m => m.Name == "Bob");
        Assert.Contains(result, m => m.Name == "Bobby");
    }

    [Fact]
    public void ByName_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(TeamMemberFilter.ByName(Members, "zed"));
    }
}
