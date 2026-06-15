namespace RustPlusBot.Discord;

/// <summary>Discord connection configuration, bound from the "Discord" config section.</summary>
public sealed class DiscordOptions
{
    /// <summary>The bot token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, deletes all global application commands on startup before
    /// registering the bot's commands. Use as a one-shot to clear stale commands left by another
    /// bot that previously used this application, then leave disabled for normal runs.
    /// </summary>
    public bool ResetCommandsOnStartup { get; set; }
}
