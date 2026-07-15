namespace RustPlusBot.Features.StorageMonitors.Posting;

/// <summary>Posts/edits a storage monitor embed in #storagemonitors by message id, self-healing a deleted message.</summary>
internal interface IStorageMonitorChannelPoster
{
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The #storagemonitors channel id.</param>
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

    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #storagemonitors channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted (or the failure swallowed).</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
}
