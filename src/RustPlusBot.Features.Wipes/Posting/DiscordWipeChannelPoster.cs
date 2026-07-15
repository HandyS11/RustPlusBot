using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Wipes.Posting;

/// <summary>Posts wipe announcements in #events via the gateway client. Untested integration shim.</summary>
/// <param name="client">The Discord socket client (raw send so the optional @everyone mention resolves).</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordWipeChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordWipeChannelPoster> logger) : IWipeChannelPoster
{
    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, string? content, Embed embed, CancellationToken cancellationToken)
    {
        try
        {
            var options = new RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not ITextChannel channel)
            {
                return;
            }

            await channel.SendMessageAsync(
                    content,
                    embed: embed,
                    options: options,
                    allowedMentions: AllowedMentions.All)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the wipe loop; swallow the failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Posting the wipe announcement in channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
