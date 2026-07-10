using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Relaying;

/// <summary>Bundles the channel/chat collaborators injected into <see cref="EventRelay"/>.</summary>
/// <param name="Locator">Resolves the #events Discord channel id.</param>
/// <param name="Poster">Posts embeds to the Discord channel.</param>
/// <param name="TeamChatSender">Broadcasts the in-game team-chat line.</param>
internal sealed record EventRelayChannels(
    IEventChannelLocator Locator,
    IEventChannelPoster Poster,
    IBotTeamChatSender TeamChatSender);

/// <summary>Posts every live event to #events AND in-game team chat; tracks rig state.</summary>
/// <param name="classifier">Classifies raw marker deltas into domain events.</param>
/// <param name="state">Tracks active markers and recent events per server.</param>
/// <param name="renderer">Renders events as embeds and in-game lines.</param>
/// <param name="channels">Bundles the channel/chat collaborators.</param>
/// <param name="rigStore">Tracks oil-rig state (Apply on Activated).</param>
/// <param name="scopeFactory">Opens scopes to read guild culture.</param>
internal sealed class EventRelay(
    MarkerEventClassifier classifier,
    EventStateStore state,
    EventEmbedRenderer renderer,
    EventRelayChannels channels,
    RigStateStore rigStore,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Handles one <see cref="MapMarkersChangedEvent"/>: updates state, posts embeds, broadcasts in-game.</summary>
    /// <param name="evt">The marker delta.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the delta has been processed.</returns>
    public async Task RelayAsync(MapMarkersChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var events = classifier.Classify(evt);
        state.Apply(evt, events);
        if (events.Count == 0)
        {
            return;
        }

        var (culture, gridStyle) = await GetRenderSettingsAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        var channelId = await channels.Locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var e in events)
        {
            await channels.TeamChatSender
                .SendAsync(evt.GuildId, evt.ServerId, renderer.RenderLine(e, culture, gridStyle), cancellationToken)
                .ConfigureAwait(false);
            if (channelId is { } id)
            {
                await channels.Poster.PostAsync(id, renderer.Render(e, culture, gridStyle), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Handles one <see cref="RigStateChangedEvent"/>: applies activation, posts an embed, broadcasts in-game.</summary>
    /// <param name="evt">The rig boundary event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the rig event has been processed.</returns>
    public async Task RelayRigAsync(RigStateChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.Kind == RigEventKind.Activated)
        {
            rigStore.Apply(evt);
        }

        var (culture, gridStyle) = await GetRenderSettingsAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        await channels.TeamChatSender
            .SendAsync(evt.GuildId, evt.ServerId, renderer.RenderRigLine(evt, culture, gridStyle), cancellationToken)
            .ConfigureAwait(false);

        var channelId = await channels.Locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is { } id)
        {
            await channels.Poster.PostAsync(id, renderer.RenderRig(evt, culture, gridStyle), cancellationToken)
                .ConfigureAwait(false);
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
