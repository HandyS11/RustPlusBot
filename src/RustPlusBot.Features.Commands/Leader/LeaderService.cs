using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Leader;

/// <summary>One selectable team member for the <c>/leader</c> promote select.</summary>
/// <param name="SteamId">The member's Steam64 id.</param>
/// <param name="Name">The member's in-game name.</param>
/// <param name="IsLeader">True if this member is the current team leader.</param>
internal sealed record LeaderMemberOption(ulong SteamId, string Name, bool IsLeader);

/// <summary>The members to offer, or an error message to show instead.</summary>
/// <param name="Members">The selectable members (empty when <paramref name="ErrorMessage"/> is set).</param>
/// <param name="ErrorMessage">A localized message to show instead of a select, or null to proceed.</param>
internal sealed record LeaderTeamResult(IReadOnlyList<LeaderMemberOption> Members, string? ErrorMessage);

/// <summary>The testable logic behind <c>/leader</c>: fetch live members, then promote one.</summary>
/// <param name="query">The live-server query seam.</param>
/// <param name="localizer">Resolves the result/error messages.</param>
internal sealed class LeaderService(IRustServerQuery query, ICommandLocalizer localizer)
{
    /// <summary>Fetches the current team for the promote select, or an error message.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The members to offer, or an error message.</returns>
    public async Task<LeaderTeamResult> GetMembersAsync(
        ulong guildId,
        Guid serverId,
        string culture,
        CancellationToken cancellationToken)
    {
        var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return new LeaderTeamResult([], localizer.Get("leader.notconnected", culture));
        }

        if (team.Members.Count == 0)
        {
            return new LeaderTeamResult([], localizer.Get("leader.noteam", culture));
        }

        var members = team.Members
            .Select(m => new LeaderMemberOption(m.SteamId, m.Name, m.SteamId == team.LeaderSteamId))
            .ToList();
        return new LeaderTeamResult(members, null);
    }

    /// <summary>Promotes a member and returns the localized result message.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="steamId">The Steam64 id to promote.</param>
    /// <param name="memberName">The member's name (for the success message).</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The localized result message.</returns>
    public async Task<string> PromoteAsync(
        ulong guildId,
        Guid serverId,
        ulong steamId,
        string memberName,
        string culture,
        CancellationToken cancellationToken)
    {
        var promoted = await query.PromoteToLeaderAsync(guildId, serverId, steamId, cancellationToken)
            .ConfigureAwait(false);
        return promoted
            ? localizer.Get("leader.success", culture, memberName)
            : localizer.Get("leader.failure", culture);
    }
}
