using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Shared Discord channel post/edit boilerplate: fetch, options, self-heal, broad-catch — plus a
///     render gate that skips edits whose content is identical to the last successful send, keeping
///     boot primes and periodic republishes out of Discord's per-channel PATCH rate-limit bucket.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="gate">The per-message no-op edit detector.</param>
public sealed class DiscordChannelMessenger(DiscordSocketClient client, RenderGate gate)
{
    /// <summary>Request timeout generous enough to ride out a queued rate-limit burst (default is 15 s).</summary>
    private const int RequestTimeoutMs = 30_000;

    /// <summary>
    ///     Edits the message by id (self-healing on 404 by reposting) or posts a new one.
    ///     Identical re-renders are skipped without calling Discord's edit endpoint.
    ///     Returns the message id, or null on failure.
    /// </summary>
    /// <param name="channelId">The target channel id.</param>
    /// <param name="messageId">The existing message id to edit, or null to post a new one.</param>
    /// <param name="embed">The embed to post or update.</param>
    /// <param name="components">The message components to post or update.</param>
    /// <param name="logger">The caller's logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = CreateOptions(cancellationToken);
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not ITextChannel channel)
            {
                return null;
            }

            var canonical = RenderCanonicalizer.Canonicalize(embed, components);
            if (messageId is { } id)
            {
                // Inner try: some Discord.Net versions THROW (HttpException 404/Unknown Message)
                // rather than return null for a deleted message. Catch it and fall through to repost
                // so the self-heal path always runs.
                try
                {
                    var existing = await channel.GetMessageAsync(id, options: options).ConfigureAwait(false);
                    if (existing is IUserMessage userMessage)
                    {
                        if (!gate.ShouldSend(id, canonical))
                        {
                            // Same content as the last successful send: don't spend the PATCH bucket.
                            return userMessage.Id;
                        }

                        await userMessage.ModifyAsync(m =>
                        {
                            m.Embed = embed;
                            m.Components = components;
                        }, options).ConfigureAwait(false);
                        gate.Commit(id, canonical);
                        return userMessage.Id;
                    }

                    // Message was deleted (returned null / not a user message); fall through to repost.
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Deleted/unknown message; fall through to repost and return the new id.
#pragma warning disable CA1848, CA1873 // Use LoggerMessage delegates / avoid expensive log-arg evaluation — plain logger.Log is fine for a shared helper (no source-gen partial context); ulong boxing is negligible vs. the caught exception.
                    logger.LogDebug(ex, "Embed {MessageId} in channel {ChannelId} was deleted; reposting.", id,
                        channelId);
#pragma warning restore CA1848, CA1873
                }

                // The tracked message is gone; the repost below re-keys the gate under the new id.
                gate.Invalidate(id);
            }

            var posted = await channel
                .SendMessageAsync(embed: embed, options: options, components: components)
                .ConfigureAwait(false);
            gate.Commit(posted.Id, canonical);
            return posted.Id;
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the relay; report failure as null.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (messageId is { } failedId)
            {
                // Outcome unknown (e.g. timeout mid-flight): forget the entry so the next render retries.
                gate.Invalidate(failedId);
            }

#pragma warning disable CA1848, CA1873 // Use LoggerMessage delegates / avoid expensive log-arg evaluation — plain logger.Log is fine for a shared helper (no source-gen partial context); ulong boxing is negligible vs. the caught exception.
            logger.LogWarning(ex, "Posting/editing an embed in channel {ChannelId} failed.", channelId);
#pragma warning restore CA1848, CA1873
            return null;
        }
    }

    /// <summary>Posts an embed fire-and-forget; Discord hiccups are logged and swallowed.</summary>
    /// <param name="channelId">The target channel id.</param>
    /// <param name="embed">The embed to post.</param>
    /// <param name="logger">The caller's logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task PostAsync(
        ulong channelId,
        Embed embed,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = CreateOptions(cancellationToken);
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
#pragma warning disable CA1848, CA1873 // Use LoggerMessage delegates / avoid expensive log-arg evaluation — plain logger.Log is fine for a shared helper (no source-gen partial context); ulong boxing is negligible vs. the caught exception.
            logger.LogWarning(ex, "Posting an embed to channel {ChannelId} failed.", channelId);
#pragma warning restore CA1848, CA1873
        }
    }

    private static RequestOptions CreateOptions(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken, RetryMode = RetryMode.AlwaysRetry, Timeout = RequestTimeoutMs,
    };
}
