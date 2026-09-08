using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Switches.Relaying;

/// <summary>Keeps switch embeds in sync: live state changes re-render; a drop from Connected marks them unreachable.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="locator">Resolves the #switches channel id.</param>
/// <param name="poster">Posts/edits switch embeds.</param>
/// <param name="renderer">Renders switch embeds.</param>
internal sealed class SwitchStateRelay(
    IServiceScopeFactory scopeFactory,
    ISwitchChannelLocator locator,
    ISwitchChannelPoster poster,
    SwitchEmbedRenderer renderer)
{
    /// <summary>Handles a live state change: persist + re-render the switch's embed.</summary>
    /// <param name="evt">The switch state change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered.</returns>
    public Task HandleStateChangedAsync(SwitchStateChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return ApplyAndRenderAsync(
            evt.GuildId,
            evt.ServerId,
            evt.EntityId,
            async (store, ct) =>
            {
                await UpdateStateAsync(store, evt, ct).ConfigureAwait(false);
                return true;
            },
            _ => evt.IsActive,
            cancellationToken);
    }

    /// <summary>Handles an in-game device trigger: ignore ids this relay doesn't manage, else persist + re-render.</summary>
    /// <param name="evt">The device-triggered event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered (or the id was ignored).</returns>
    public Task HandleDeviceTriggeredAsync(SmartDeviceTriggeredEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return ApplyAndRenderAsync(
            evt.GuildId,
            evt.ServerId,
            evt.EntityId,
            async (store, ct) =>
            {
                if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, ct).ConfigureAwait(false))
                {
                    return false; // not a switch this relay manages (e.g. an alarm) — ignore.
                }

                await UpdateStateAsync(store, evt, ct).ConfigureAwait(false);
                return true;
            },
            _ => evt.IsActive,
            cancellationToken);
    }

    /// <summary>Handles a per-device reachability change: ignore foreign entities, else persist + re-render.</summary>
    /// <param name="evt">The device-reachability-changed event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered (or the id was ignored).</returns>
    public Task HandleReachabilityChangedAsync(
        DeviceReachabilityChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return ApplyAndRenderAsync(
            evt.GuildId,
            evt.ServerId,
            evt.EntityId,
            async (store, ct) =>
            {
                if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, ct).ConfigureAwait(false))
                {
                    return false; // not a switch this relay manages — ignore.
                }

                await store.SetReachabilityAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Reachability, ct)
                    .ConfigureAwait(false);
                return true;
            },
            sw => sw.LastIsActive,
            cancellationToken);
    }

    /// <summary>
    /// Opens a scope, applies a mutation to the switch's state, then re-renders its embed if the mutation
    /// reports success and the switch still exists. Shared by the three per-entity handlers above, whose
    /// only difference is how the store gets mutated and which state counts as "active" for the render.
    /// </summary>
    /// <param name="guildId">The Discord guild id.</param>
    /// <param name="serverId">The paired Rust+ server id.</param>
    /// <param name="entityId">The switch's in-game entity id.</param>
    /// <param name="mutateAsync">Applies the store mutation; returns <see langword="false"/> to skip the render.</param>
    /// <param name="isActiveSelector">Picks the on/off state to render from the persisted switch.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been re-rendered (or the mutation was skipped).</returns>
    private async Task ApplyAndRenderAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        Func<ISwitchStore, CancellationToken, Task<bool>> mutateAsync,
        Func<SmartSwitch, bool?> isActiveSelector,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            if (!await mutateAsync(store, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var sw = await store.GetAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false);
            if (sw is null)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, guildId, cancellationToken)
                .ConfigureAwait(false);
            await RenderAsync(store, sw, isActiveSelector(sw), guildId, serverId, culture, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Task UpdateStateAsync(ISwitchStore store,
        SwitchStateChangedEvent evt,
        CancellationToken cancellationToken) =>
        store.UpdateStateAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.IsActive, cancellationToken);

    private static Task UpdateStateAsync(ISwitchStore store,
        SmartDeviceTriggeredEvent evt,
        CancellationToken cancellationToken) =>
        store.UpdateStateAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.IsActive, cancellationToken);

    /// <summary>Handles a connection-status change: a drop from Connected marks its switch embeds unreachable.</summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every affected embed has been re-rendered.</returns>
    public async Task HandleConnectionStatusAsync(
        ConnectionStatusChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.IsConnected || !evt.WasConnected)
        {
            // Connected: the supervisor's prime path republishes real state — nothing to do.
            // Never-connected in this process (boot, reconnect-loop repeats): keep the last-run
            // embeds; only a drop from Connected sweeps them to unreachable.
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            var switches = await store.ListByServerAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (switches.Count == 0)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var sw in switches)
            {
                await RenderAsync(store, sw, isActive: null, evt.GuildId, evt.ServerId, culture, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task RenderAsync(
        ISwitchStore store,
        SmartSwitch sw,
        bool? isActive,
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

        var (embed, components) = renderer.RenderSwitch(sw, isActive, culture);
        var newMessageId = await poster.EnsureAsync(channel, sw.MessageId, embed, components, cancellationToken)
            .ConfigureAwait(false);
        if (newMessageId is { } mid && mid != sw.MessageId)
        {
            await store.SetMessageIdAsync(guildId, serverId, sw.EntityId, mid, cancellationToken)
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
