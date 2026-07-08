namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #info channel id (for posting the static map image).</summary>
public interface IInfoChannelLocator
{
    /// <summary>Gets the #info Discord channel id for (guild, server), or null if not provisioned.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The channel id, or null.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
