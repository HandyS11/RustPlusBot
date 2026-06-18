using Discord;

namespace RustPlusBot.Features.Players.Posting;

/// <summary>Posts a player-event embed to a Discord channel.</summary>
internal interface IPlayerChannelPoster
{
    /// <summary>Posts <paramref name="embed"/> to the channel.</summary>
    /// <param name="channelId">The Discord channel snowflake.</param>
    /// <param name="embed">The embed to post.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken);
}
