using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Events.Posting;

/// <summary>Posts event embeds to Discord text channels via the gateway client.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordEventChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordEventChannelPoster> logger)
    : IEventChannelPoster
{
    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
    {
        try
        {
            if (await client.GetChannelAsync(channelId).ConfigureAwait(false) is not ITextChannel channel)
            {
                return;
            }

            await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the relay.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting an event embed to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
