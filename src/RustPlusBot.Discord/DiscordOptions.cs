namespace RustPlusBot.Discord;

/// <summary>Discord connection configuration, bound from the "Discord" config section.</summary>
public sealed class DiscordOptions
{
    /// <summary>The bot token.</summary>
    public string Token { get; set; } = string.Empty;
}
