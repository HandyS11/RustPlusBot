using RustPlusApi.Data;
using RustPlusApi.Data.Clans;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ClanMappingTests
{
    [Fact]
    public void Maps_no_clan_error_to_NoClan()
    {
        var result = ClanMapping.FromResponse(false, RustPlusErrorCode.NoClan, null);

        Assert.Equal(ClanProbeStatus.NoClan, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Maps_any_other_error_to_Unavailable()
    {
        var result = ClanMapping.FromResponse(false, RustPlusErrorCode.NotFound, null);

        Assert.Equal(ClanProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Maps_a_missing_error_code_to_Unavailable()
    {
        var result = ClanMapping.FromResponse(false, null, null);

        Assert.Equal(ClanProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Maps_success_with_null_payload_to_Unavailable()
    {
        // A success carrying no data is not evidence of "no clan"; preserve the last known state.
        var result = ClanMapping.FromResponse(true, null, null);

        Assert.Equal(ClanProbeStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Maps_a_populated_clan_to_a_snapshot()
    {
        var info = new ClanInfo
        {
            ClanId = 42,
            Name = "Wolves",
            Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Creator = 7UL,
            Motd = "hold the line",
            MotdTimestamp = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            MotdAuthor = 9UL,
            Logo = [1, 2, 3],
            Color = 255,
            MaxMemberCount = 8,
            Score = 1234,
            Roles =
            [
                new ClanRole
                {
                    RoleId = 1, Rank = 0, Name = "Leader", CanSetMotd = true
                }
            ],
            Members =
            [
                new ClanMember
                {
                    SteamId = 7UL,
                    RoleId = 1,
                    Joined = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    LastSeen = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    Notes = "founder",
                    Online = true,
                },
            ],
            Invites =
            [
                new ClanInvite
                {
                    SteamId = 11UL, Recruiter = 7UL, Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
        };

        var result = ClanMapping.FromResponse(true, null, info);

        Assert.Equal(ClanProbeStatus.HasClan, result.Status);
        var snapshot = Assert.IsType<ClanSnapshot>(result.Snapshot);
        Assert.Equal(42L, snapshot.ClanId);
        Assert.Equal("Wolves", snapshot.Name);
        Assert.Equal(7UL, snapshot.Creator);
        Assert.Equal(1234L, snapshot.Score);
        Assert.Equal(8, snapshot.MaxMemberCount);
        Assert.Equal("hold the line", snapshot.Motd);
        Assert.Equal(new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero), snapshot.MotdTimestamp);
        Assert.Equal(9UL, snapshot.MotdAuthor);
        Assert.Equal(255, snapshot.Color);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), snapshot.Created);
        Assert.Single(snapshot.Roles);
        Assert.True(snapshot.Roles[0].CanSetMotd);
        Assert.Equal(1, snapshot.Roles[0].RoleId);
        Assert.Equal(0, snapshot.Roles[0].Rank);
        Assert.Equal("Leader", snapshot.Roles[0].Name);
        Assert.Single(snapshot.Members);
        Assert.True(snapshot.Members[0].Online);
        Assert.Equal("founder", snapshot.Members[0].Notes);
        Assert.Equal(1, snapshot.Members[0].RoleId);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), snapshot.Members[0].Joined);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), snapshot.Members[0].LastSeen);
        Assert.Single(snapshot.Invites);
        Assert.Equal(11UL, snapshot.Invites[0].SteamId);
        Assert.Equal(7UL, snapshot.Invites[0].Recruiter);
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), snapshot.Invites[0].Timestamp);
        Assert.NotNull(snapshot.LogoHash);
    }

    [Fact]
    public void Treats_a_null_Online_flag_as_offline()
    {
        // ClanMember.Online is bool? in the API; null must not be rendered as online.
        var info = NewClan(members:
        [
            new ClanMember
            {
                SteamId = 7UL, RoleId = 1, Online = null
            }
        ]);

        var result = ClanMapping.FromResponse(true, null, info);

        Assert.False(result.Snapshot!.Members[0].Online);
    }

    [Fact]
    public void Treats_timestamps_as_utc()
    {
        var info = NewClan() with
        {
            Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
        };

        var result = ClanMapping.FromResponse(true, null, info);

        Assert.Equal(TimeSpan.Zero, result.Snapshot!.Created.Offset);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), result.Snapshot.Created.UtcDateTime);
    }

    [Fact]
    public void Hashes_a_missing_logo_to_null_and_equal_logos_to_equal_hashes()
    {
        Assert.Null(ClanMapping.HashLogo(null));
        Assert.Null(ClanMapping.HashLogo([]));
        Assert.Equal(ClanMapping.HashLogo([1, 2, 3]), ClanMapping.HashLogo([1, 2, 3]));
        Assert.NotEqual(ClanMapping.HashLogo([1, 2, 3]), ClanMapping.HashLogo([3, 2, 1]));
    }

    private static ClanInfo NewClan(IEnumerable<ClanMember>? members = null) =>
        new()
        {
            ClanId = 1,
            Name = "C",
            Creator = 1UL,
            Roles = [],
            Members = members ?? [],
            Invites = [],
        };
}
