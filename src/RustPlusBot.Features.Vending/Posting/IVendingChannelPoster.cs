namespace RustPlusBot.Features.Vending.Posting;

/// <summary>Posts/edits a vending embed in #vending by message id, self-healing a deleted message.</summary>
internal interface IVendingChannelPoster
{
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The #vending channel id.</param>
    /// <param name="messageId">The known message id, or null to post fresh.</param>
    /// <param name="embed">The embed to show.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The (possibly new) message id, or null on failure.</returns>
    Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        global::Discord.Embed embed,
        CancellationToken cancellationToken);

    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #vending channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted or the failure swallowed.</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
}
