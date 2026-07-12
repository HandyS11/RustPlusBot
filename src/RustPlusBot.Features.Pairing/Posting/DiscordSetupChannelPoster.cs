using Discord;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Pairing.Posting;

/// <summary>Posts/edits server-pairing prompts in #setup by message id. Untested integration shim.</summary>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordSetupChannelPoster(
    DiscordChannelMessenger messenger,
    ILogger<DiscordSetupChannelPoster> logger) : ISetupChannelPoster
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
