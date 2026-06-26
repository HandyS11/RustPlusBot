using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Features.StorageMonitors.Modules;

/// <summary>Thin handler for the #storagemonitors pairing prompt + Refresh/Rename. Any guild member.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="query">Live socket read (for Refresh).</param>
/// <param name="eventBus">Publishes a triggered event to drive an embed refresh.</param>
public sealed class StorageMonitorComponentModule(
    IServiceScopeFactory scopeFactory,
    IRustServerQuery query,
    IEventBus eventBus) : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";

    /// <summary>Accepts a pending pairing prompt and starts managing the monitor.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.AcceptPrefix + "*")]
    public async Task AcceptAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<StorageMonitorPairingCoordinator>();
            var accepted = await coordinator
                .TryAcceptAsync(Context.Guild.Id, serverId, entityId, Context.User.Id, CancellationToken.None)
                .ConfigureAwait(false);
            await FollowupAsync(accepted ? "Storage monitor added." : "That monitor is already managed.",
                ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>Dismisses a pending pairing prompt and removes the transient prompt message.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.DismissPrefix + "*")]
    public async Task DismissAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<StorageMonitorPairingCoordinator>();
            coordinator.TryDismiss(Context.Guild.Id, serverId, entityId);
        }

        await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        await RespondAsync("Dismissed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Re-reads the monitor's contents and republishes them so the embed refreshes.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.RefreshPrefix + "*")]
    public async Task RefreshAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var contents = await query
            .GetStorageContentsAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
            .ConfigureAwait(false);
        if (contents is null)
        {
            await FollowupAsync("Storage monitor is unreachable right now.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await eventBus
            .PublishAsync(new StorageMonitorTriggeredEvent(Context.Guild.Id, serverId, entityId, contents))
            .ConfigureAwait(false);
        await FollowupAsync("Refreshed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Opens the rename modal, carrying the target tail in the modal custom id.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.RenamePrefix + "*")]
    public async Task RenamePromptAsync(string tail)
    {
        if (!TryParse(tail, out _, out _) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondWithModalAsync<StorageMonitorRenameModal>(StorageMonitorComponentIds.RenameModalPrefix + tail)
            .ConfigureAwait(false);
    }

    /// <summary>Persists the new name, then republishes current contents so the embed refreshes.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    /// <param name="modal">The submitted rename modal.</param>
    [ModalInteraction(StorageMonitorComponentIds.RenameModalPrefix + "*")]
    public async Task RenameSubmitAsync(string tail, StorageMonitorRenameModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var name = string.IsNullOrWhiteSpace(modal.Name)
            ? "Storage Monitor " + entityId.ToString(CultureInfo.InvariantCulture)
            : modal.Name.Trim();
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            await store.RenameAsync(Context.Guild.Id, serverId, entityId, name, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Re-read (may be null if unreachable) and republish so the relay re-renders the renamed embed.
        var contents = await query
            .GetStorageContentsAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
            .ConfigureAwait(false);
        if (contents is not null)
        {
            await eventBus
                .PublishAsync(new StorageMonitorTriggeredEvent(Context.Guild.Id, serverId, entityId, contents))
                .ConfigureAwait(false);
        }

        await FollowupAsync("Renamed.", ephemeral: true).ConfigureAwait(false);
    }

    private static bool TryParse(string tail, out Guid serverId, out ulong entityId)
    {
        serverId = Guid.Empty;
        entityId = 0UL;
        if (tail is null)
        {
            return false;
        }

        var parts = tail.Split(':');
        return parts.Length == 2
               && Guid.TryParse(parts[0], out serverId)
               && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out entityId);
    }

    private async Task DeletePromptMessageSafeAsync()
    {
        try
        {
            // The source message of a component interaction is the prompt that carries the button.
            if (Context.Interaction is IComponentInteraction component)
            {
                await component.Message.DeleteAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Best-effort prompt cleanup; a delete failure is non-fatal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Ignore: the prompt is transient and harmless if it lingers.
            _ = ex;
        }
    }
}
