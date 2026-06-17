using Discord;

namespace RustPlusBot.Features.Events.Posting;

/// <summary>Posts an event embed to a Discord channel.</summary>
internal interface IEventChannelPoster
{
    /// <summary>Posts <paramref name="embed"/> to the channel.</summary>
    /// <param name="channelId">The target Discord channel id.</param>
    /// <param name="embed">The embed to post.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been posted.</returns>
    Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken);
}
