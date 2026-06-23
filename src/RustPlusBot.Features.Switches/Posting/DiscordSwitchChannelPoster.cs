using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Switches.Posting;

/// <summary>Posts/edits switch embeds in #switches by message id. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordSwitchChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordSwitchChannelPoster> logger) : ISwitchChannelPoster
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
