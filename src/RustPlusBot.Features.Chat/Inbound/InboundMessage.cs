namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>A Discord message observed in a channel, reduced to what the bridge needs (no Discord.Net types).</summary>
/// <param name="AuthorIsBotOrWebhook">True if the author is a bot or a webhook (ignored to avoid loops).</param>
/// <param name="ChannelId">The channel the message was posted in.</param>
/// <param name="DisplayName">The author's guild display name.</param>
/// <param name="Content">The message text.</param>
internal sealed record InboundMessage(bool AuthorIsBotOrWebhook, ulong ChannelId, string DisplayName, string Content);
