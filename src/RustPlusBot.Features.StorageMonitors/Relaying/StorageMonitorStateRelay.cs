using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.StorageMonitors.Relaying;

/// <summary>Keeps storage monitor embeds in sync: trigger events re-render with the carried contents; a non-Connected server marks them unreachable.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="locator">Resolves the #storagemonitors channel id.</param>
/// <param name="poster">Posts/edits storage monitor embeds.</param>
/// <param name="renderer">Renders storage monitor embeds.</param>
/// <param name="query">Reads live storage contents when a device becomes reachable.</param>
internal sealed class StorageMonitorStateRelay(
    IServiceScopeFactory scopeFactory,
    IStorageMonitorChannelLocator locator,
    IStorageMonitorChannelPoster poster,
    StorageMonitorEmbedRenderer renderer,
    IRustServerQuery query)
{
    /// <summary>Handles a storage monitor trigger: ignore unmanaged ids; else render the event's contents directly.</summary>
    /// <param name="evt">The storage monitor triggered event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered (or the id was ignored).</returns>
    public async Task HandleTriggeredAsync(StorageMonitorTriggeredEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return; // not a storage monitor this relay manages — ignore.
            }

            var monitor = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                .ConfigureAwait(false);
            if (monitor is null)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken)
                .ConfigureAwait(false);
            await RenderAsync(store, monitor, evt.Contents, evt.GuildId, evt.ServerId, culture, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Handles a per-device reachability change: ignore foreign entities, else persist + re-render.
    /// A device that (re)became <see cref="DeviceReachability.Reachable"/> is re-read so the embed shows its
    /// contents instead of a stale unreachable banner (the prime races this handler on a second bus loop).</summary>
    /// <param name="evt">The device-reachability-changed event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered (or the id was ignored).</returns>
    public async Task HandleReachabilityChangedAsync(
        DeviceReachabilityChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return; // not a storage monitor this relay manages — ignore.
            }

            await store.SetReachabilityAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Reachability,
                    cancellationToken)
                .ConfigureAwait(false);
            var monitor = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                .ConfigureAwait(false);
            if (monitor is null)
            {
                return;
            }

            var contents = evt.Reachability == DeviceReachability.Reachable
                ? await query.GetStorageContentsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken)
                .ConfigureAwait(false);
            await RenderAsync(store, monitor, contents, evt.GuildId, evt.ServerId, culture, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Handles a connection-status change: a non-Connected server marks its storage monitor embeds unreachable.</summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every affected embed has been re-rendered.</returns>
    public async Task HandleConnectionStatusAsync(
        ConnectionStatusChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connections = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await connections.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (state is { Status: ConnectionStatus.Connected })
            {
                // The supervisor's prime path republishes real state on connect; nothing to do here.
                return;
            }

            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            var monitors = await store.ListByServerAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (monitors.Count == 0)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var monitor in monitors)
            {
                await RenderAsync(store, monitor, contents: null, evt.GuildId, evt.ServerId, culture,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RenderAsync(
        IStorageMonitorStore store,
        SmartStorageMonitor monitor,
        StorageContentsSnapshot? contents,
        ulong guildId,
        Guid serverId,
        string culture,
        CancellationToken cancellationToken)
    {
        var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } channel)
        {
            return;
        }

        var (embed, components) = renderer.RenderMonitor(monitor, contents, culture);
        var newMessageId = await poster.EnsureAsync(channel, monitor.MessageId, embed, components, cancellationToken)
            .ConfigureAwait(false);
        if (newMessageId is { } mid && mid != monitor.MessageId)
        {
            await store.SetMessageIdAsync(guildId, serverId, monitor.EntityId, mid, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> GetCultureAsync(
        IServiceProvider provider,
        ulong guildId,
        CancellationToken cancellationToken)
    {
        var store = provider.GetRequiredService<IWorkspaceStore>();
        return await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
    }
}
