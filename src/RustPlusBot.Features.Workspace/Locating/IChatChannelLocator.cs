using RustPlusBot.Abstractions.Chat;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Resolves the per-server chat channel for one <see cref="ChatChannelKind"/> in both directions
/// (for the chat bridge). One implementation is registered per kind, so the bridge can select the
/// locator matching the channel it is relaying.
/// </summary>
public interface IChatChannelLocator
{
    /// <summary>Gets the in-game chat channel this locator maps to.</summary>
    ChatChannelKind Kind { get; }

    /// <summary>
    /// Gets the Discord channel id of this kind's chat channel for (<paramref name="guildId"/>,
    /// <paramref name="serverId"/>), or null.
    /// </summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a Discord channel id to its (guild, server) if it is a chat channel of this kind, else null.
    /// </summary>
    /// <param name="channelId">The Discord channel snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The owning (guild, server), or null if the channel is not of this kind.</returns>
    Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId, CancellationToken cancellationToken);
}
