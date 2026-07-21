using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>
/// Relays one received in-game chat line into the Discord channel for its <see cref="ChatChannelKind"/>,
/// dropping our own echoes and command invocations.
/// </summary>
/// <param name="locators">The registered chat channel locators, indexed by kind.</param>
/// <param name="poster">Posts the line via webhook.</param>
/// <param name="dedup">Tracks lines the bridge relayed into the game so their echoes can be dropped.</param>
/// <param name="scopeFactory">Opens a scope to read the scoped <see cref="IMuteStore"/> command prefix.</param>
internal sealed class ChatRelay(
    IEnumerable<IChatChannelLocator> locators,
    IChatWebhookPoster poster,
    RelayDedupBuffer dedup,
    IServiceScopeFactory scopeFactory)
{
    private readonly Dictionary<ChatChannelKind, IChatChannelLocator> _locators = locators.ToDictionary(l => l.Kind);

    /// <summary>Relays one received in-game chat line into its Discord channel.</summary>
    /// <param name="line">The received line.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the line has been relayed or dropped.</returns>
    public async Task RelayAsync(RelayedChatLine line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.FromActivePlayer && line.Message.StartsWith(BotTeamChat.Prefix, StringComparison.Ordinal))
        {
            return; // Bot-originated line echoing back; the channel carries only human discussion.
        }

        if (line.FromActivePlayer &&
            dedup.TryConsume(line.Kind, (line.GuildId, line.ServerId), line.Message))
        {
            return; // Our own relayed line echoing back; do not re-post.
        }

        if (!_locators.TryGetValue(line.Kind, out var locator))
        {
            return;
        }

        // The locator is an in-memory cache, so resolve the channel first: unmapped servers exit
        // before the per-message prefix query below.
        var channelId = await locator.GetChannelIdAsync(line.GuildId, line.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        // A command invocation (e.g. "!pop") gets its reply in game; the bare trigger line is noise in Discord.
        var prefix = await GetCommandPrefixAsync(line.GuildId, line.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(prefix) &&
            line.Message.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        await poster.PostAsync(line.Kind, channelId.Value, line.SenderName, line.Message, cancellationToken)
            .ConfigureAwait(false);
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
