namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when an in-game team chat line is received, so the chat bridge can relay it to Discord.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose socket received the line.</param>
/// <param name="SenderSteamId">The Steam64 id of the in-game sender.</param>
/// <param name="SenderName">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
/// <param name="FromActivePlayer">True when the sender is the bot's active player (used to drop relay echoes).</param>
public sealed record TeamMessageReceivedEvent(
    ulong GuildId,
    Guid ServerId,
    ulong SenderSteamId,
    string SenderName,
    string Message,
    bool FromActivePlayer);
