using Discord;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the static map image + embed to #info, replacing the bot's prior image post.</summary>
internal interface IInfoMapPoster
{
    /// <summary>Deletes the bot's prior #info image post (if any) and posts the new embed + PNG.</summary>
    /// <param name="channelId">The #info channel id.</param>
    /// <param name="embed">The map embed (title, size/seed, RustMaps link).</param>
    /// <param name="pngBytes">The map image PNG.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the post is issued.</returns>
    Task PostAsync(ulong channelId, Embed embed, byte[] pngBytes, CancellationToken cancellationToken);
}
