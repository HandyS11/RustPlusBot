namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The marker prefix identifying bot-originated in-game team-chat lines.</summary>
public static class BotTeamChat
{
    /// <summary>
    /// Prepended (with a trailing space) to every bot-originated team-chat line so the relay can
    /// drop the echo instead of re-posting it to Discord. Never localized.
    /// </summary>
    public const string Prefix = "[R+]";
}
