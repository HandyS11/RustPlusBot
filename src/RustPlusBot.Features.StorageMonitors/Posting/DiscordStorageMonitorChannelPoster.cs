using Discord;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.StorageMonitors.Posting;

/// <summary>Posts/edits storage monitor embeds in #storagemonitors by message id. Untested integration shim.</summary>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordStorageMonitorChannelPoster(
    DiscordChannelMessenger messenger,
    ILogger<DiscordStorageMonitorChannelPoster> logger) : IStorageMonitorChannelPoster
{
    /// <inheritdoc />
    public Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        CancellationToken cancellationToken)
        => messenger.EnsureAsync(channelId, messageId, embed, components, logger, cancellationToken);
}
