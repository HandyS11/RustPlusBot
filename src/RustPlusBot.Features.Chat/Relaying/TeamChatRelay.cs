using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>Relays one received in-game team message into its Discord #teamchat channel, dropping our own echoes.</summary>
/// <param name="locator">Resolves the target #teamchat channel.</param>
/// <param name="poster">Posts the line via webhook.</param>
/// <param name="dedup">Tracks lines the bridge relayed into the game so their echoes can be dropped.</param>
internal sealed class TeamChatRelay(
    ITeamChatChannelLocator locator,
    ITeamChatWebhookPoster poster,
    RelayDedupBuffer dedup)
{
    /// <summary>Handles one <see cref="TeamMessageReceivedEvent"/>.</summary>
    /// <param name="evt">The received team message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the line has been relayed or dropped.</returns>
    public async Task RelayAsync(TeamMessageReceivedEvent evt, CancellationToken cancellationToken)
    {
        if (evt.FromActivePlayer && evt.Message.StartsWith(BotTeamChat.Prefix, StringComparison.Ordinal))
        {
            return; // Bot-originated line echoing back; #teamchat carries only human discussion.
        }

        if (evt.FromActivePlayer && dedup.TryConsume((evt.GuildId, evt.ServerId), evt.Message))
        {
            return; // Our own relayed line echoing back; do not re-post.
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        await poster.PostAsync(channelId.Value, evt.SenderName, evt.Message, cancellationToken).ConfigureAwait(false);
    }
}
