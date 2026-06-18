using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Handles the #map layer-toggle buttons (ManageGuild).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="eventBus">Publishes a settings-changed event to trigger an immediate repaint.</param>
public sealed class MapComponentModule(IServiceScopeFactory scopeFactory, IEventBus eventBus)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Flips one layer, re-renders the control message, and triggers a map repaint.</summary>
    /// <param name="tail">The "{layer}:{serverId}" tail captured from the custom id.</param>
    [ComponentInteraction(WorkspaceComponentIds.MapTogglePrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task ToggleAsync(string tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var parts = tail.Split(':');
        if (parts.Length != 2
            || !Enum.TryParse<MapLayer>(parts[0], ignoreCase: false, out var layer)
            || !Guid.TryParse(parts[1], out var serverId))
        {
            await RespondAsync("That control wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
            var current = await store.GetAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            var newValue = !IsEnabled(current, layer);
            await store.SetLayerAsync(Context.Guild.Id, serverId, layer, newValue).ConfigureAwait(false);

            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.ReconcileServerAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
        }

        await eventBus.PublishAsync(new MapSettingsChangedEvent(Context.Guild.Id, serverId)).ConfigureAwait(false);
        await FollowupAsync("Updated map layers.", ephemeral: true).ConfigureAwait(false);
    }

    private static bool IsEnabled(MapLayerSettings settings, MapLayer layer) => layer switch
    {
        MapLayer.Grid => settings.Grid,
        MapLayer.Markers => settings.Markers,
        MapLayer.Monuments => settings.Monuments,
        MapLayer.Vendor => settings.Vendor,
        MapLayer.Players => settings.Players,
        MapLayer.Rigs => settings.Rigs,
        _ => true,
    };
}
