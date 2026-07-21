using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>
/// Relays one received in-game team message into its Discord #teamchat channel, dropping our own
/// echoes and command invocations.
/// </summary>
/// <param name="locators">The registered chat channel locators; the team one is selected from these.</param>
/// <param name="poster">Posts the line via webhook.</param>
/// <param name="dedup">Tracks lines the bridge relayed into the game so their echoes can be dropped.</param>
/// <param name="scopeFactory">Opens a scope to read the scoped <see cref="IMuteStore"/> command prefix.</param>
internal sealed class TeamChatRelay(
    IEnumerable<IChatChannelLocator> locators,
    ITeamChatWebhookPoster poster,
    RelayDedupBuffer dedup,
    IServiceScopeFactory scopeFactory)
{
    private readonly IChatChannelLocator _locator = locators.Single(l => l.Kind == ChatChannelKind.Team);

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

        if (evt.FromActivePlayer &&
            dedup.TryConsume(ChatChannelKind.Team, (evt.GuildId, evt.ServerId), evt.Message))
        {
            return; // Our own relayed line echoing back; do not re-post.
        }

        // The locator is an in-memory cache, so resolve the channel first: unmapped servers exit
        // before the per-message prefix query below.
        var channelId = await _locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        // A command invocation (e.g. "!pop") gets its reply in game; the bare trigger line is noise in Discord.
        var prefix = await GetCommandPrefixAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(prefix) &&
            evt.Message.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        await poster.PostAsync(channelId.Value, evt.SenderName, evt.Message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetCommandPrefixAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        // IMuteStore is scoped, so resolve it from a fresh scope rather than capturing it on this singleton.
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var muteStore = scope.ServiceProvider.GetRequiredService<IMuteStore>();
            return await muteStore.GetPrefixAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }
    }
}
