using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Maps a RustPlusApi <see cref="RustPlusApi.Data.TeamInfo"/> to the bot's decoupled
/// <see cref="TeamInfoSnapshot"/>. Shared by the polled read and the pushed <c>team_changed</c> event so
/// the two paths can never drift.</summary>
internal static class TeamInfoMapping
{
    /// <summary>Projects a RustPlusApi team-info model onto a <see cref="TeamInfoSnapshot"/>.</summary>
    /// <param name="teamInfo">The RustPlusApi team info (from a poll response or a team_changed broadcast).</param>
    /// <returns>The decoupled snapshot the tracker and events consume.</returns>
    public static TeamInfoSnapshot ToSnapshot(RustPlusApi.Data.TeamInfo teamInfo)
    {
        // CONFIRMED (2.0.0-beta.6): MemberInfo exposes SteamId/Name?/X/Y/IsOnline/IsAlive plus
        // LastSpawnTime/LastDeathTime as Unspecified-kind DateTimes that are actually UTC.
        var members = (teamInfo.Members ?? [])
            .Select(m => new TeamMemberSnapshot(
                m.SteamId,
                m.Name ?? string.Empty,
                m.X,
                m.Y,
                m.IsOnline,
                m.IsAlive,
                new DateTimeOffset(DateTime.SpecifyKind(m.LastSpawnTime, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(m.LastDeathTime, DateTimeKind.Utc))))
            .ToList();
        var deathNote = teamInfo.DeathNote is { } dn
            ? ((float X, float Y)?)(dn.X, dn.Y)
            : null;
        return new TeamInfoSnapshot(teamInfo.LeaderSteamId, members, deathNote);
    }
}
