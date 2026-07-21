using RustPlusBot.Abstractions.Chat;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The outcome of relaying a Discord message into an in-game chat channel.</summary>
public enum ChatSendResult
{
    /// <summary>The message was handed to the live socket.</summary>
    Sent = 0,

    /// <summary>There is no live socket for that (guild, server) right now.</summary>
    NotConnected = 1,

    /// <summary>A live socket exists but the send failed.</summary>
    Failed = 2,
}

/// <summary>
/// Relays a message into one of a server's in-game chat channels (implemented by the connection supervisor).
/// The <see cref="ChatChannelKind"/> selects the destination, so one seam serves both team and clan chat.
/// </summary>
public interface IChatSender
{
    /// <summary>
    /// Sends <paramref name="message"/> to the <paramref name="kind"/> chat channel on the live socket for
    /// (<paramref name="guildId"/>, <paramref name="serverId"/>).
    /// </summary>
    /// <param name="kind">The in-game chat channel to send to.</param>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="message">The message text to relay.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send result.</returns>
    Task<ChatSendResult> SendAsync(ChatChannelKind kind,
        ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken);
}
