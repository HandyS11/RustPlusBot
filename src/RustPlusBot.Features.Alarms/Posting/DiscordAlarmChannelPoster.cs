using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Alarms.Posting;

/// <summary>Posts/edits alarm embeds in #alarms by message id. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordAlarmChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordAlarmChannelPoster> logger) : IAlarmChannelPoster
{
    /// <inheritdoc />
    public async Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        global::Discord.Embed embed,
        global::Discord.MessageComponent components,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new global::Discord.RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not global::Discord.ITextChannel channel)
            {
                return null;
            }

            if (messageId is { } id)
            {
                // Inner try: some Discord.Net versions THROW (HttpException 404/Unknown Message)
                // rather than return null for a deleted message. Catch it and fall through to repost
                // so the self-heal path always runs.
                try
                {
                    var existing = await channel.GetMessageAsync(id, options: options).ConfigureAwait(false);
                    if (existing is global::Discord.IUserMessage userMessage)
                    {
                        await userMessage.ModifyAsync(m =>
                        {
                            m.Embed = embed;
                            m.Components = components;
                        }, options).ConfigureAwait(false);
                        return userMessage.Id;
                    }

                    // Message was deleted (returned null / not a user message); fall through to repost.
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Deleted/unknown message; fall through to repost and return the new id.
                    LogMessageMissing(logger, ex, channelId, id);
                }
            }

            var posted = await channel
                .SendMessageAsync(embed: embed, options: options, components: components)
                .ConfigureAwait(false);
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
            LogEnsureFailed(logger, ex, channelId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SendEveryonePingAsync(ulong channelId, string content, CancellationToken cancellationToken)
    {
        try
        {
            var options = new global::Discord.RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not global::Discord.ITextChannel channel)
            {
                return;
            }

            await channel.SendMessageAsync(
                    content,
                    options: options,
                    allowedMentions: global::Discord.AllowedMentions.All)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting/editing an alarm embed in channel {ChannelId} failed.")]
    private static partial void LogEnsureFailed(ILogger logger, Exception exception, ulong channelId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Alarm embed {MessageId} in channel {ChannelId} was deleted; reposting.")]
    private static partial void
        LogMessageMissing(ILogger logger, Exception exception, ulong channelId, ulong messageId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Sending @everyone ping in channel {ChannelId} failed.")]
    private static partial void LogPingFailed(ILogger logger, Exception exception, ulong channelId);
}
