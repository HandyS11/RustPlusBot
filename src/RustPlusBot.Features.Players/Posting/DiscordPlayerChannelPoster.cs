using Discord;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Players.Posting;

/// <summary>Posts player-event embeds to Discord text channels via the gateway client.</summary>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordPlayerChannelPoster(
    DiscordChannelMessenger messenger,
    ILogger<DiscordPlayerChannelPoster> logger)
    : IPlayerChannelPoster
{
    /// <inheritdoc />
    public Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
        => messenger.PostAsync(channelId, embed, logger, cancellationToken);
}
