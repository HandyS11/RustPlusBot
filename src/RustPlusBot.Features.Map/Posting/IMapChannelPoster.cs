namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the rendered map image to a Discord channel, replacing the prior bot message.</summary>
internal interface IMapChannelPoster
{
    /// <summary>Deletes the bot's prior map message (if any) and posts the new PNG.</summary>
    /// <param name="channelId">The #map channel id.</param>
    /// <param name="pngBytes">The rendered PNG.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the post is issued.</returns>
    Task PostAsync(ulong channelId, byte[] pngBytes, CancellationToken cancellationToken);
}
