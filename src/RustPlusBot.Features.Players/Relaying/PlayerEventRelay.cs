using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Relaying;

/// <summary>Posts every player transition to #player-events AND in-game team chat.</summary>
/// <param name="renderer">Renders transitions as embeds and in-game lines.</param>
/// <param name="locator">Resolves the #player-events Discord channel id.</param>
/// <param name="poster">Posts embeds to the Discord channel.</param>
/// <param name="teamChatSender">Broadcasts the in-game team-chat line.</param>
/// <param name="scopeFactory">Opens scopes to read guild culture.</param>
internal sealed class PlayerEventRelay(
    PlayerEventRenderer renderer,
    IPlayerEventChannelLocator locator,
    IPlayerChannelPoster poster,
    IBotTeamChatSender teamChatSender,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Handles one <see cref="PlayerStateChangedEvent"/>: posts embeds and broadcasts in-game.</summary>
    /// <param name="evt">The player state changed event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task RelayAsync(PlayerStateChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.Transitions.Count == 0)
        {
            return;
        }

        var (culture, gridStyle) = await GetRenderSettingsAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var t in evt.Transitions)
        {
            await teamChatSender
                .SendAsync(evt.GuildId, evt.ServerId, renderer.RenderLine(t, evt.Dimensions, culture, gridStyle),
                    cancellationToken)
                .ConfigureAwait(false);
            if (channelId is { } id)
            {
                await poster.PostAsync(id, renderer.Render(t, evt.Dimensions, culture, gridStyle), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<(string Culture, MapGridStyle GridStyle)> GetRenderSettingsAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            var mapSettings = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
            var settings = await mapSettings.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            return (culture, settings.GridStyle);
        }
    }
}
