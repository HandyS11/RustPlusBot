namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One currently-AFK team member.</summary>
/// <param name="SteamId">Steam64 id.</param>
/// <param name="Name">In-game display name.</param>
/// <param name="StillFor">How long the member has been continuously still.</param>
public sealed record AfkMember(ulong SteamId, string Name, TimeSpan StillFor);

/// <summary>Reads the live AFK state computed by the connection poll loop.</summary>
public interface IAfkState
{
    /// <summary>Gets the currently-AFK members, or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The currently-AFK members, or null when there is no live socket.</returns>
    Task<IReadOnlyList<AfkMember>?> GetAfkMembersAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken);
}
