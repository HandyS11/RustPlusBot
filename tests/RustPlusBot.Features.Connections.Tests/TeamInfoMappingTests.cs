using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamInfoMappingTests
{
    [Fact]
    public void ToSnapshot_MapsMembersLeaderAndDeathNote()
    {
        var teamInfo = new RustPlusApi.Data.TeamInfo
        {
            LeaderSteamId = 100UL,
            DeathNote = new RustPlusApi.Data.Notes.DeathNote { X = 12f, Y = 34f },
            Members =
            [
                new RustPlusApi.Data.MemberInfo
                {
                    SteamId = 100UL,
                    Name = "Alice",
                    X = 1f,
                    Y = 2f,
                    IsOnline = true,
                    IsAlive = false,
                    LastSpawnTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    LastDeathTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
                },
            ],
        };

        var snapshot = TeamInfoMapping.ToSnapshot(teamInfo);

        Assert.Equal(100UL, snapshot.LeaderSteamId);
        Assert.Equal((12f, 34f), snapshot.DeathNote);
        var m = Assert.Single(snapshot.Members);
        Assert.Equal(100UL, m.SteamId);
        Assert.Equal("Alice", m.Name);
        Assert.True(m.IsOnline);
        Assert.False(m.IsAlive);
        // Unspecified game timestamps are read as UTC.
        Assert.Equal(DateTimeKind.Utc, m.LastDeathTimeUtc.UtcDateTime.Kind);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), m.LastDeathTimeUtc);
    }

    [Fact]
    public void ToSnapshot_NullMembersAndNoDeathNote_YieldEmptyMembersAndNullNote()
    {
        var teamInfo = new RustPlusApi.Data.TeamInfo { LeaderSteamId = 7UL, Members = null, DeathNote = null };

        var snapshot = TeamInfoMapping.ToSnapshot(teamInfo);

        Assert.Equal(7UL, snapshot.LeaderSteamId);
        Assert.Empty(snapshot.Members);
        Assert.Null(snapshot.DeathNote);
    }
}
