namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Resolves the per-server #claninfo channel (bot-to-Discord direction only). There is no reverse
/// lookup because nothing reads messages back out of #claninfo.
/// </summary>
public interface IClanInfoChannelLocator
{
    /// <summary>Gets the #claninfo channel id for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
