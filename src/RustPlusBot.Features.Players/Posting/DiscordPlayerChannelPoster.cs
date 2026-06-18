using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Players.Posting;

/// <summary>Posts player-event embeds to Discord text channels via the gateway client.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordPlayerChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordPlayerChannelPoster> logger)
    : IPlayerChannelPoster
{
    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
    {
        try
        {
            var options = new RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false) is not ITextChannel channel)
            {
                return;
            }

            await channel.SendMessageAsync(embed: embed, options: options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Cancellation (shutdown) is not a post failure; let the relay loop unwind cleanly.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the relay.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting a player-event embed to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
