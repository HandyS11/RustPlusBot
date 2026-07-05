namespace RustPlusBot.Features.Connections.Listening;

/// <summary>
/// Relays a bot-originated line (command reply, event/player/alarm notification) into a server's
/// in-game team chat, prefixed with <see cref="BotTeamChat.Prefix"/> so its echo is never re-posted
/// to the Discord #teamchat channel. Player speech bridged from Discord uses
/// <see cref="ITeamChatSender"/> instead.
/// </summary>
public interface IBotTeamChatSender
{
    /// <summary>Sends <paramref name="message"/>, prefixed, to the live socket for (<paramref name="guildId"/>, <paramref name="serverId"/>).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="message">The unprefixed message text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send result.</returns>
    Task<TeamChatSendResult> SendAsync(ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken);
}
