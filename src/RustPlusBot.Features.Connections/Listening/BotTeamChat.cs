namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The marker prefix identifying bot-originated in-game team-chat lines.</summary>
public static class BotTeamChat
{
    /// <summary>
    /// Marker leading every bot-originated team-chat line so the relay can drop the echo instead of
    /// re-posting it to Discord. The constant carries no trailing space; <see cref="BotTeamChatSender"/>
    /// inserts one space between the prefix and the message. Never localized.
    /// </summary>
    public const string Prefix = "[R+]";
}
