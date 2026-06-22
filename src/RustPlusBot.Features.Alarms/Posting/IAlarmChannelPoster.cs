namespace RustPlusBot.Features.Alarms.Posting;

/// <summary>Posts/edits an alarm embed in #alarms by message id, self-healing a deleted message.</summary>
internal interface IAlarmChannelPoster
{
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The #alarms channel id.</param>
    /// <param name="messageId">The known embed message id, or null to post fresh.</param>
    /// <param name="embed">The embed to show.</param>
    /// <param name="components">The control row.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The (possibly new) message id, or null on failure.</returns>
    Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        global::Discord.Embed embed,
        global::Discord.MessageComponent components,
        CancellationToken cancellationToken);

    /// <summary>Sends an @everyone ping message in the given channel.</summary>
    /// <param name="channelId">The #alarms channel id.</param>
    /// <param name="content">The message content (typically includes @everyone).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been sent (or silently swallowed on failure).</returns>
    Task SendEveryonePingAsync(ulong channelId, string content, CancellationToken cancellationToken);
}
