using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Clans.Posting;

/// <summary>
/// Real <see cref="IClanFeedPoster"/>. Feed lines are plain bot messages rather than webhook
/// impersonation: this is the bot narrating clan events, not a player speaking. Untested
/// integration shim.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordClanFeedPoster(
    DiscordSocketClient client,
    ILogger<DiscordClanFeedPoster> logger) : IClanFeedPoster
{
    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, string text, CancellationToken cancellationToken)
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

            await channel.SendMessageAsync(text, options: options, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the clan state loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting a clan feed line to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
