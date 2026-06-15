namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #teamchat channel in both directions (for the chat bridge).</summary>
public interface ITeamChatChannelLocator
{
    /// <summary>Gets the Discord channel id of the #teamchat for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Resolves a Discord channel id to its (guild, server) if it is a #teamchat channel, else null.</summary>
    /// <param name="channelId">The Discord channel snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The owning (guild, server), or null if the channel is not a #teamchat.</returns>
    Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId, CancellationToken cancellationToken);
}
