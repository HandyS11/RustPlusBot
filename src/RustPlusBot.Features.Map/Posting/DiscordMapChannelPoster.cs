using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the map image to Discord by deleting the bot's prior message and reposting. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordMapChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordMapChannelPoster> logger) : IMapChannelPoster
{
    /// <summary>Scan the last N messages for the bot's own prior map post (only one is expected).</summary>
    private const int RecentMessageScan = 10;

    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, byte[] pngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
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
                await channel.SendFileAsync(stream, "map.png", options: options, allowedMentions: AllowedMentions.None)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown: let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the map loop.
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
        foreach (var message in batch.Where(m => m.Author.Id == client.CurrentUser.Id))
        {
            await message.DeleteAsync(options).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting the map image to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Deleting prior map messages in channel {ChannelId} failed; reposting anyway.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, ulong channelId);
}
