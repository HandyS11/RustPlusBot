using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.StorageMonitors.Posting;

/// <summary>Posts/edits storage monitor embeds in #storagemonitors by message id. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordStorageMonitorChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordStorageMonitorChannelPoster> logger) : IStorageMonitorChannelPoster
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
}
