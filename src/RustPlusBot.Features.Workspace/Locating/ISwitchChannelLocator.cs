namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #switches channel (used to post/edit switch embeds).</summary>
public interface ISwitchChannelLocator
{
    /// <summary>Gets the Discord channel id of #switches for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
