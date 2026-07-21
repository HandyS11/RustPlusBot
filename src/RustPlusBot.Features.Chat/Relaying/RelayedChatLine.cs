using RustPlusBot.Abstractions.Chat;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>One received in-game chat line, normalised across channel kinds.</summary>
/// <param name="Kind">The in-game chat channel the line was received on.</param>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose socket received the line.</param>
/// <param name="SenderName">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
/// <param name="FromActivePlayer">True when the sender is the bot's active player (used to drop relay echoes).</param>
internal sealed record RelayedChatLine(
    ChatChannelKind Kind,
    ulong GuildId,
    Guid ServerId,
    string SenderName,
    string Message,
    bool FromActivePlayer);
