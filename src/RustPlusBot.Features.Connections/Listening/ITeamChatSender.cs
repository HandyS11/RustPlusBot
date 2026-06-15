namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The outcome of relaying a Discord message into in-game team chat.</summary>
public enum TeamChatSendResult
{
    /// <summary>The message was handed to the live socket.</summary>
    Sent = 0,

    /// <summary>There is no live socket for that (guild, server) right now.</summary>
    NotConnected = 1,

    /// <summary>A live socket exists but the send failed.</summary>
    Failed = 2,
}

/// <summary>Relays a message into a server's in-game team chat (implemented by the connection supervisor).</summary>
public interface ITeamChatSender
{
    /// <summary>Sends <paramref name="message"/> to the live socket for (<paramref name="guildId"/>, <paramref name="serverId"/>).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="message">The message text to relay.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send result.</returns>
    Task<TeamChatSendResult> SendAsync(ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken);
}
