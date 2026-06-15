using System.Globalization;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>Turns a Discord #teamchat message into an in-game relay (record-then-send), reporting the outcome.</summary>
/// <param name="locator">Resolves whether/which server a channel maps to.</param>
/// <param name="sender">Relays the formatted line into the game.</param>
/// <param name="dedup">Records the relayed line so its in-game echo can be dropped.</param>
internal sealed class TeamChatInboundProcessor(
    ITeamChatChannelLocator locator,
    ITeamChatSender sender,
    RelayDedupBuffer dedup)
{
    /// <summary>Processes one observed Discord message.</summary>
    /// <param name="message">The reduced message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done with the message.</returns>
    public async Task<InboundOutcome> ProcessAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        if (message.AuthorIsBotOrWebhook || string.IsNullOrWhiteSpace(message.Content))
        {
            return InboundOutcome.Ignored;
        }

        var target = await locator.ResolveAsync(message.ChannelId, cancellationToken).ConfigureAwait(false);
        if (target is not { } t)
        {
            return InboundOutcome.Ignored;
        }

        var key = (t.GuildId, t.ServerId);
        var text = string.Create(CultureInfo.InvariantCulture, $"[{message.DisplayName}] {message.Content}");

        // Record BEFORE sending: the in-game echo can arrive before SendAsync returns. Unused entries expire.
        dedup.Record(key, text);
        var result = await sender.SendAsync(t.GuildId, t.ServerId, text, cancellationToken).ConfigureAwait(false);

        return result == TeamChatSendResult.Sent ? InboundOutcome.Sent : InboundOutcome.Failed;
    }
}
