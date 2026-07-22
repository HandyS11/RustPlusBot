namespace RustPlusBot.Features.Clans.Posting;

/// <summary>Posts a plain feed line into the #claninfo channel.</summary>
internal interface IClanFeedPoster
{
    /// <summary>Posts one feed line.</summary>
    /// <param name="channelId">The #claninfo channel snowflake.</param>
    /// <param name="text">The already-localized line to post.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the post has been attempted.</returns>
    Task PostAsync(ulong channelId, string text, CancellationToken cancellationToken);
}
