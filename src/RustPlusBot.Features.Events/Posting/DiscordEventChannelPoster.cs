using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Events.Posting;

/// <summary>Posts event embeds to Discord text channels via the gateway client.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordEventChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordEventChannelPoster> logger)
    : IEventChannelPoster
{
    /// <inheritdoc />
    public Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
        => DiscordChannelMessenger.PostAsync(client, channelId, embed, logger, cancellationToken);
}
