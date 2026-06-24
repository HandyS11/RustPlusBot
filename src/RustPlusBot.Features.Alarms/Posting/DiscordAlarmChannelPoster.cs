using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Alarms.Posting;

/// <summary>Posts/edits alarm embeds in #alarms by message id. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordAlarmChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordAlarmChannelPoster> logger) : IAlarmChannelPoster
{
    /// <inheritdoc />
    public Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        CancellationToken cancellationToken)
        => DiscordChannelMessenger.EnsureAsync(client, channelId, messageId, embed, components, logger,
            cancellationToken);

    /// <inheritdoc />
    public async Task SendEveryonePingAsync(ulong channelId, string content, CancellationToken cancellationToken)
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
                    options: options,
                    allowedMentions: AllowedMentions.All)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the alarm ping; swallow the failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPingFailed(logger, ex, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sending @everyone ping in channel {ChannelId} failed.")]
    private static partial void LogPingFailed(ILogger logger, Exception exception, ulong channelId);
}
