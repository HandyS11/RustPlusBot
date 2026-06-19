namespace RustPlusBot.Features.Switches.Posting;

/// <summary>Posts/edits a switch embed in #switches by message id, self-healing a deleted message.</summary>
internal interface ISwitchChannelPoster
{
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The #switches channel id.</param>
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
}
