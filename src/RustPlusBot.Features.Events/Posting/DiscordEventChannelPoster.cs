using Discord;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Events.Posting;

/// <summary>Posts event embeds to Discord text channels via the gateway client.</summary>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordEventChannelPoster(
    DiscordChannelMessenger messenger,
    ILogger<DiscordEventChannelPoster> logger)
    : IEventChannelPoster
{
    /// <inheritdoc />
    public Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
        => messenger.PostAsync(channelId, embed, logger, cancellationToken);
}
