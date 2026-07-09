using Discord;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Maintains the single static #info map message, editing it in place so the channel never shows two.</summary>
internal interface IInfoMapPoster
{
    /// <summary>
    /// Creates or updates the one #info map message. When <paramref name="existingMessageId"/> is a live
    /// bot message it is edited in place (embed + image swapped); otherwise any stray prior map posts are
    /// removed and a fresh message is posted. Returns the id of the resulting message to track for next time.
    /// </summary>
    /// <param name="channelId">The #info channel id.</param>
    /// <param name="existingMessageId">The id of the previously-posted map message, or null on first post.</param>
    /// <param name="embed">The map embed (title, size/seed, RustMaps link).</param>
    /// <param name="pngBytes">The map image PNG.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The id of the current map message, or null if it could not be posted.</returns>
    Task<ulong?> UpsertAsync(ulong channelId,
        ulong? existingMessageId,
        Embed embed,
        byte[] pngBytes,
        CancellationToken cancellationToken);
}
