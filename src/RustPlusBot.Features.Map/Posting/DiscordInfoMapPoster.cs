using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the static map image to #info by deleting the bot's prior image post and reposting. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordInfoMapPoster(
    DiscordSocketClient client,
    ILogger<DiscordInfoMapPoster> logger) : IInfoMapPoster
{
    private const int RecentMessageScan = 10;

    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, Embed embed, byte[] pngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embed);
        ArgumentNullException.ThrowIfNull(pngBytes);
        try
        {
            var options = new RequestOptions { CancelToken = cancellationToken };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false) is not ITextChannel channel)
            {
                return;
            }

            try
            {
                await DeletePriorBotMessagesAsync(channel, options).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a failed delete must not abort the repost (best-effort).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDeleteFailed(logger, ex, channelId);
            }

            var stream = new MemoryStream(pngBytes);
            await using (stream.ConfigureAwait(false))
            {
                await channel.SendFileAsync(stream, "map.png", embed: embed, options: options,
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the info-map loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    private async Task DeletePriorBotMessagesAsync(ITextChannel channel, RequestOptions options)
    {
        var batch = await channel.GetMessagesAsync(RecentMessageScan, options: options).FlattenAsync()
            .ConfigureAwait(false);
        // Delete only our prior IMAGE posts (they carry a file attachment); leave the reconciled
        // #info status embed (no attachment) for the workspace reconciler.
        foreach (var message in batch.Where(m => m.Author.Id == client.CurrentUser.Id && m.Attachments.Count > 0))
        {
            await message.DeleteAsync(options).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting the #info map image to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Deleting prior #info map messages in channel {ChannelId} failed; reposting anyway.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, ulong channelId);
}
