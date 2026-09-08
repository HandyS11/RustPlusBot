using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Devices.Posting;

namespace RustPlusBot.Features.Vending.Posting;

/// <summary>Posts/edits vending embeds in #vending by message id. Untested integration shim.</summary>
/// <param name="client">The Discord socket client (used directly for raw message deletes).</param>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordVendingChannelPoster(
    DiscordSocketClient client,
    DiscordChannelMessenger messenger,
    ILogger<DiscordVendingChannelPoster> logger)
    : DiscordDeviceChannelPoster(client, messenger, logger), IVendingChannelPoster
{
    /// <inheritdoc />
    /// <remarks>Vending embeds carry no interactive controls, so this always posts an empty component row.</remarks>
    public Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        CancellationToken cancellationToken)
        => base.EnsureAsync(channelId, messageId, embed, new ComponentBuilder().Build(), cancellationToken);
}
