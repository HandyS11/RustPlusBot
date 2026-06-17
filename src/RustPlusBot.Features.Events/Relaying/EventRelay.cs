using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Relaying;

/// <summary>Classifies one marker delta, updates state, and posts one embed per event to #events.</summary>
/// <param name="classifier">Classifies raw marker deltas into domain events.</param>
/// <param name="state">Tracks active markers and recent events per server.</param>
/// <param name="renderer">Renders events as Discord embeds.</param>
/// <param name="locator">Resolves the #events Discord channel id.</param>
/// <param name="poster">Posts embeds to the Discord channel.</param>
/// <param name="scopeFactory">Opens scopes to read guild culture.</param>
internal sealed class EventRelay(
    MarkerEventClassifier classifier,
    EventStateStore state,
    EventEmbedRenderer renderer,
    IEventChannelLocator locator,
    IEventChannelPoster poster,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Handles one <see cref="MapMarkersChangedEvent"/>.</summary>
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

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        var culture = await GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        foreach (var e in events)
        {
            var embed = renderer.Render(e, culture);
            await poster.PostAsync(channelId.Value, embed, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            return await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        }
    }
}
