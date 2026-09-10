namespace RustPlusBot.Persistence.Chat;

/// <summary>A chat-relay webhook the bot created: enough to post through it without asking Discord.</summary>
/// <param name="Id">The webhook snowflake.</param>
/// <param name="Token">The webhook token, in plaintext.</param>
public readonly record struct ChatWebhook(ulong Id, string Token);

/// <summary>
/// Remembers the webhooks the chat relay creates, so a restart posts through the webhook it already
/// owns instead of re-discovering one by name — a rename would otherwise orphan the old webhook and
/// silently create a duplicate alongside it.
/// </summary>
public interface IChatWebhookStore
{
    /// <summary>Reads the webhook recorded for a channel, or <c>null</c> when there is none.</summary>
    /// <param name="guildId">The owning guild.</param>
    /// <param name="channelId">The channel the webhook posts to.</param>
    /// <param name="key">The relay's stable key for the webhook (the chat kind).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The webhook, or <c>null</c>.</returns>
    Task<ChatWebhook?> GetAsync(
        ulong guildId,
        ulong channelId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Records a webhook, replacing whatever was recorded for the same channel and key.</summary>
    /// <param name="guildId">The owning guild.</param>
    /// <param name="channelId">The channel the webhook posts to.</param>
    /// <param name="key">The relay's stable key for the webhook (the chat kind).</param>
    /// <param name="webhookId">The webhook snowflake Discord returned.</param>
    /// <param name="token">The webhook token, in plaintext; it is protected before it is written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the record has been written.</returns>
    Task SaveAsync(
        ulong guildId,
        ulong channelId,
        string key,
        ulong webhookId,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the record for a channel, so the next post re-discovers or re-creates the webhook. Call it
    /// when the recorded webhook turns out to be unusable — deleted in Discord, or its token revoked.
    /// </summary>
    /// <param name="guildId">The owning guild.</param>
    /// <param name="channelId">The channel the webhook posts to.</param>
    /// <param name="key">The relay's stable key for the webhook (the chat kind).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the record is gone.</returns>
    Task ForgetAsync(
        ulong guildId,
        ulong channelId,
        string key,
        CancellationToken cancellationToken = default);
}
