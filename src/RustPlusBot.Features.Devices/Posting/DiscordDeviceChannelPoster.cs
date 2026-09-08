using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Devices.Posting;

/// <summary>
/// Shared Discord.Net adapter body for a device-channel poster: edits/posts an embed through the
/// gated <see cref="DiscordChannelMessenger"/> and deletes a message with the self-heal broad-catch
/// used across every device feature (#switches, #storagemonitors, #vending, …). Feature posters derive
/// from this for their own interface and logger category. Untested integration shim.
/// </summary>
/// <param name="client">The Discord socket client (used directly for raw message deletes).</param>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
public abstract partial class DiscordDeviceChannelPoster(
    DiscordSocketClient client,
    DiscordChannelMessenger messenger,
    ILogger logger) : IDeviceChannelPoster
{
    /// <inheritdoc />
    public Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        CancellationToken cancellationToken)
        => messenger.EnsureAsync(channelId, messageId, embed, components, logger, cancellationToken);

    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The device channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted (or the failure swallowed).</returns>
    public async Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken)
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

            await channel.DeleteMessageAsync(messageId, options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup (or already-deleted message) must not crash the purge.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeleteFailed(logger, ex, messageId, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Deleting message {MessageId} in channel {ChannelId} failed (may already be gone).")]
    private static partial void LogDeleteFailed(ILogger logger,
        Exception exception,
        ulong messageId,
        ulong channelId);
}
