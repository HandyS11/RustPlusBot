namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>What the inbound processor did with a Discord message.</summary>
internal enum InboundOutcome
{
    /// <summary>Not a #teamchat message, empty, or a bot/webhook author — nothing relayed.</summary>
    Ignored = 0,

    /// <summary>Relayed into the game.</summary>
    Sent = 1,

    /// <summary>Belongs to a #teamchat but could not be relayed (no live socket or send failed).</summary>
    Failed = 2,
}
