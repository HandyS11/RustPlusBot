using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>
/// Turns a Discord chat-channel message into an in-game relay (record-then-send), reporting the outcome.
/// The channel kind comes from whichever registered locator claims the channel.
/// </summary>
/// <param name="locators">The registered chat channel locators; the one claiming the channel supplies the kind.</param>
/// <param name="sender">Relays the formatted line into the game.</param>
/// <param name="dedup">Records the relayed line so its in-game echo can be dropped.</param>
/// <param name="scopeFactory">Opens a scope to read the scoped <see cref="IMuteStore"/> mute gate.</param>
internal sealed class ChatInboundProcessor(
    IEnumerable<IChatChannelLocator> locators,
    IChatSender sender,
    RelayDedupBuffer dedup,
    IServiceScopeFactory scopeFactory)
{
    private readonly IReadOnlyList<IChatChannelLocator> _locators = [.. locators];

    /// <summary>Processes one observed Discord message.</summary>
    /// <param name="message">The reduced message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done with the message.</returns>
    public async Task<InboundOutcome> ProcessAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.AuthorIsBotOrWebhook || string.IsNullOrWhiteSpace(message.Content))
        {
            return InboundOutcome.Ignored;
        }

        // Whichever locator claims this channel supplies both its owning guild and server and its channel
        // kind. A channel that no locator claims is not a chat channel, so it is ignored.
        (ulong GuildId, Guid ServerId)? target = null;
        var kind = ChatChannelKind.Team;
        foreach (var locator in _locators)
        {
            if (await locator.ResolveAsync(message.ChannelId, cancellationToken).ConfigureAwait(false) is { } hit)
            {
                target = hit;
                kind = locator.Kind;
                break;
            }
        }

        if (target is not { } t)
        {
            return InboundOutcome.Ignored;
        }

        // A muted server silences ALL bot->game output: ignore fully (neither record a dedup entry nor send).
        // IMuteStore is scoped, so resolve it from a fresh scope rather than capturing it on this singleton.
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var muteStore = scope.ServiceProvider.GetRequiredService<IMuteStore>();
            if (await muteStore.GetMutedAsync(t.GuildId, t.ServerId, cancellationToken).ConfigureAwait(false))
            {
                return InboundOutcome.Ignored;
            }
        }

        var key = (t.GuildId, t.ServerId);
        var text = string.Create(CultureInfo.InvariantCulture, $"[{message.DisplayName}] {message.Content}");

        // Record BEFORE sending: the in-game echo can arrive before SendAsync returns. Unused entries expire.
        dedup.Record(kind, key, text);
        var result = await sender
            .SendAsync(kind, t.GuildId, t.ServerId, text, cancellationToken)
            .ConfigureAwait(false);

        return result == ChatSendResult.Sent ? InboundOutcome.Sent : InboundOutcome.Failed;
    }
}
