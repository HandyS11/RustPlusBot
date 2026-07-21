using RustPlusBot.Abstractions.Chat;

namespace RustPlusBot.Features.Chat.Webhooks;

/// <summary>Posts an in-game chat line into its Discord chat channel as the player (via a webhook).</summary>
public interface IChatWebhookPoster
{
    /// <summary>Posts <paramref name="message"/> to channel <paramref name="channelId"/> impersonating <paramref name="username"/>.</summary>
    /// <param name="kind">The in-game chat channel the line came from.</param>
    /// <param name="channelId">The target Discord channel snowflake.</param>
    /// <param name="username">The webhook display name (the in-game player's Steam name).</param>
    /// <param name="message">The message text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been posted.</returns>
    Task PostAsync(
        ChatChannelKind kind,
        ulong channelId,
        string username,
        string message,
        CancellationToken cancellationToken);
}
