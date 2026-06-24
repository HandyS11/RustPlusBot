using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Switches.Modules;

/// <summary>Thin handler for the #switches pairing prompt + control buttons + rename modal. Any guild member.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="query">Live socket read/control.</param>
/// <param name="eventBus">Publishes a state-changed event to drive an embed refresh.</param>
public sealed class SwitchComponentModule(
    IServiceScopeFactory scopeFactory,
    IRustServerQuery query,
    IEventBus eventBus) : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";

    /// <summary>Accepts a pending pairing prompt and starts managing the switch.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(SwitchComponentIds.AcceptPrefix + "*")]
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
            var coordinator = scope.ServiceProvider.GetRequiredService<SwitchPairingCoordinator>();
            var accepted = await coordinator
                .TryAcceptAsync(Context.Guild.Id, serverId, entityId, Context.User.Id, CancellationToken.None)
                .ConfigureAwait(false);
            await FollowupAsync(accepted ? "Switch added." : "That switch is already managed.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Dismisses a pending pairing prompt and removes the transient prompt message.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(SwitchComponentIds.DismissPrefix + "*")]
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
            var coordinator = scope.ServiceProvider.GetRequiredService<SwitchPairingCoordinator>();
            coordinator.TryDismiss(Context.Guild.Id, serverId, entityId);
        }

        // Delete the actual prompt message that hosts this button (the component interaction's source
        // message) — not the ephemeral interaction response. Best-effort: a delete failure is non-fatal.
        await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        await RespondAsync("Dismissed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Turns the switch on.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(SwitchComponentIds.OnPrefix + "*")]
    public Task OnAsync(string tail) => SetAsync(tail, value: true);

    /// <summary>Turns the switch off.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(SwitchComponentIds.OffPrefix + "*")]
    public Task OffAsync(string tail) => SetAsync(tail, value: false);

    /// <summary>Strobes the switch on, then re-reads and publishes the settled state.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(SwitchComponentIds.StrobePrefix + "*")]
    public async Task StrobeAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var ok = await query
            .StrobeSmartSwitchAsync(Context.Guild.Id, serverId, entityId, timeoutMs: 1000, value: true,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!ok)
        {
            await FollowupAsync("Switch is unreachable right now.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var state = await query.GetSmartSwitchStateAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
            .ConfigureAwait(false);
        await eventBus
            .PublishAsync(new SwitchStateChangedEvent(Context.Guild.Id, serverId, entityId, state ?? true))
            .ConfigureAwait(false);
        await FollowupAsync("Strobed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Opens the rename modal, carrying the target tail in the modal custom id.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(SwitchComponentIds.RenamePrefix + "*")]
    public async Task RenamePromptAsync(string tail)
    {
        if (!TryParse(tail, out _, out _) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        // The modal id carries the same tail so the submit handler can route.
        await RespondWithModalAsync<SwitchRenameModal>(SwitchComponentIds.RenameModalPrefix + tail)
            .ConfigureAwait(false);
    }

    /// <summary>Persists the new name, then publishes the current state so the embed refreshes.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    /// <param name="modal">The submitted rename modal.</param>
    [ModalInteraction(SwitchComponentIds.RenameModalPrefix + "*")]
    public async Task RenameSubmitAsync(string tail, SwitchRenameModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var name = string.IsNullOrWhiteSpace(modal.Name)
            ? "Switch " + entityId.ToString(CultureInfo.InvariantCulture)
            : modal.Name.Trim();
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        bool isActive;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            await store.RenameAsync(Context.Guild.Id, serverId, entityId, name, CancellationToken.None)
                .ConfigureAwait(false);
            var sw = await store.GetAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
                .ConfigureAwait(false);
            isActive = sw?.LastIsActive ?? false;
        }

        await eventBus
            .PublishAsync(new SwitchStateChangedEvent(Context.Guild.Id, serverId, entityId, isActive))
            .ConfigureAwait(false);
        await FollowupAsync("Renamed.", ephemeral: true).ConfigureAwait(false);
    }

    private async Task SetAsync(string tail, bool value)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var ok = await query.SetSmartSwitchAsync(Context.Guild.Id, serverId, entityId, value, CancellationToken.None)
            .ConfigureAwait(false);
        if (!ok)
        {
            await FollowupAsync("Switch is unreachable right now.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await eventBus.PublishAsync(new SwitchStateChangedEvent(Context.Guild.Id, serverId, entityId, value))
            .ConfigureAwait(false);
        await FollowupAsync(value ? "Turned on." : "Turned off.", ephemeral: true).ConfigureAwait(false);
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
