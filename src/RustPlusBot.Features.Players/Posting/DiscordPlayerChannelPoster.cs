using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Players.Posting;

/// <summary>Posts player-event embeds to Discord text channels via the gateway client.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordPlayerChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordPlayerChannelPoster> logger)
    : IPlayerChannelPoster
{
    /// <inheritdoc />
    public Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
        => DiscordChannelMessenger.PostAsync(client, channelId, embed, logger, cancellationToken);
}
