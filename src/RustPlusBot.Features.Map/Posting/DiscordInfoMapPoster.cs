using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Maintains the single #info map message, editing it in place so the channel never shows two. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordInfoMapPoster(
    DiscordSocketClient client,
    ILogger<DiscordInfoMapPoster> logger) : IInfoMapPoster
{
    private const int RecentMessageScan = 10;

    /// <inheritdoc />
    public async Task<ulong?> UpsertAsync(ulong channelId,
        ulong? existingMessageId,
        Embed embed,
        byte[] pngBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embed);
        ArgumentNullException.ThrowIfNull(pngBytes);
        var options = new RequestOptions
        {
            CancelToken = cancellationToken
        };
        try
        {
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false) is not ITextChannel channel)
            {
                return existingMessageId;
            }

            // Edit the one existing message in place — no delete+repost, so the channel never flashes two.
            if (existingMessageId is { } id
                && await TryEditAsync(channel, id, embed, pngBytes, options).ConfigureAwait(false))
            {
                return id;
            }

            // No live message to edit (first post, or it was deleted, or the edit failed): sweep any stray
            // prior map posts, then post a single fresh message and track its id.
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
                var posted = await channel.SendFileAsync(stream, "map.png", embed: embed, options: options,
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
                return posted.Id;
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
            return existingMessageId;
        }
    }

    private async Task<bool> TryEditAsync(ITextChannel channel,
        ulong messageId,
        Embed embed,
        byte[] pngBytes,
        RequestOptions options)
    {
        try
        {
            if (await channel.GetMessageAsync(messageId, options: options).ConfigureAwait(false)
                    is not IUserMessage existing
                || existing.Author.Id != client.CurrentUser.Id)
            {
                return false; // gone or not ours — fall back to a fresh post.
            }

            var stream = new MemoryStream(pngBytes);
            await using (stream.ConfigureAwait(false))
            {
                await existing.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Attachments = new[]
                    {
                        new FileAttachment(stream, "map.png")
                    };
                }, options).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: an edit failure falls back to a fresh post below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogEditFailed(logger, ex, channel.Id);
            return false;
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
        Message = "Editing the #info map message in channel {ChannelId} failed; reposting instead.")]
    private static partial void LogEditFailed(ILogger logger, Exception exception, ulong channelId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Deleting prior #info map messages in channel {ChannelId} failed; reposting anyway.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, ulong channelId);
}
