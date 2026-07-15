using Discord;

namespace RustPlusBot.Features.Wipes.Posting;

/// <summary>Posts the one-off wipe announcement to a Discord channel.</summary>
internal interface IWipeChannelPoster
{
    /// <summary>Posts <paramref name="embed"/> (with optional mention <paramref name="content"/>) to the channel.</summary>
    /// <param name="channelId">The target Discord channel id.</param>
    /// <param name="content">Optional message content (e.g. "@everyone"), or null for embed-only.</param>
    /// <param name="embed">The announcement embed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been sent (or the failure swallowed).</returns>
    Task PostAsync(ulong channelId, string? content, Embed embed, CancellationToken cancellationToken);
}
