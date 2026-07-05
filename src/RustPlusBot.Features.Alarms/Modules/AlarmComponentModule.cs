using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Persistence.Alarms;

namespace RustPlusBot.Features.Alarms.Modules;

/// <summary>Thin handler for the #alarms pairing prompt + control buttons + rename modal. Any guild member.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="refresher">Re-renders the alarm embed on demand.</param>
/// <param name="query">Live socket read (for Refresh).</param>
/// <param name="eventBus">Publishes reachability/observed-state events to drive an embed refresh.</param>
public sealed class AlarmComponentModule(
    IServiceScopeFactory scopeFactory,
    IAlarmRefresher refresher,
    IRustServerQuery query,
    IEventBus eventBus) : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";

    /// <summary>Accepts a pending pairing prompt and starts managing the alarm.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(AlarmComponentIds.AcceptPrefix + "*")]
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
            var coordinator = scope.ServiceProvider.GetRequiredService<AlarmPairingCoordinator>();
            var accepted = await coordinator
                .TryAcceptAsync(Context.Guild.Id, serverId, entityId, Context.User.Id, CancellationToken.None)
                .ConfigureAwait(false);
            await FollowupAsync(accepted ? "Alarm added." : "That alarm is already managed.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Dismisses a pending pairing prompt and removes the transient prompt message.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(AlarmComponentIds.DismissPrefix + "*")]
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
            var coordinator = scope.ServiceProvider.GetRequiredService<AlarmPairingCoordinator>();
            coordinator.TryDismiss(Context.Guild.Id, serverId, entityId);
        }

        // Delete the actual prompt message that hosts this button (the component interaction's source
        // message) — not the ephemeral interaction response. Best-effort: a delete failure is non-fatal.
        await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        await RespondAsync("Dismissed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Toggles the @everyone ping setting for this alarm.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(AlarmComponentIds.PingTogglePrefix + "*")]
    public async Task PingToggleAsync(string tail)
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
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var current = await store.GetAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
                .ConfigureAwait(false);
            if (current is null)
            {
                await FollowupAsync("That alarm isn't managed.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await store
                .SetPingEveryoneAsync(Context.Guild.Id, serverId, entityId, !current.PingEveryone,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        await refresher.RefreshAsync(Context.Guild.Id, serverId, entityId, unreachable: false, CancellationToken.None)
            .ConfigureAwait(false);
        await FollowupAsync("Updated.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Toggles the relay-to-team-chat setting for this alarm.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(AlarmComponentIds.RelayTogglePrefix + "*")]
    public async Task RelayToggleAsync(string tail)
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
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var current = await store.GetAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
                .ConfigureAwait(false);
            if (current is null)
            {
                await FollowupAsync("That alarm isn't managed.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await store
                .SetRelayToTeamChatAsync(Context.Guild.Id, serverId, entityId, !current.RelayToTeamChat,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        await refresher.RefreshAsync(Context.Guild.Id, serverId, entityId, unreachable: false, CancellationToken.None)
            .ConfigureAwait(false);
        await FollowupAsync("Updated.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Re-reads the alarm's live state and republishes it so the embed refreshes.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(AlarmComponentIds.RefreshPrefix + "*")]
    public async Task RefreshAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var reading = await query
            .GetSmartAlarmReadingAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
            .ConfigureAwait(false);

        // Persist/render stays in the relay pipelines — this handler only reads and publishes.
        await eventBus
            .PublishAsync(new DeviceReachabilityChangedEvent(Context.Guild.Id, serverId, entityId,
                reading.Reachability))
            .ConfigureAwait(false);
        if (reading is { Reachability: DeviceReachability.Reachable, IsActive: { } isActive })
        {
            await eventBus
                .PublishAsync(new SmartDeviceStateObservedEvent(Context.Guild.Id, serverId, entityId, isActive))
                .ConfigureAwait(false);
            await FollowupAsync("Refreshed.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await FollowupAsync("Alarm is unreachable right now.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Opens the rename modal, carrying the target tail in the modal custom id.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(AlarmComponentIds.RenamePrefix + "*")]
    public async Task RenamePromptAsync(string tail)
    {
        if (!TryParse(tail, out _, out _) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        // The modal id carries the same tail so the submit handler can route.
        await RespondWithModalAsync<AlarmRenameModal>(AlarmComponentIds.RenameModalPrefix + tail)
            .ConfigureAwait(false);
    }

    /// <summary>Persists the new name, then refreshes the alarm embed.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    /// <param name="modal">The submitted rename modal.</param>
    [ModalInteraction(AlarmComponentIds.RenameModalPrefix + "*")]
    public async Task RenameSubmitAsync(string tail, AlarmRenameModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var name = string.IsNullOrWhiteSpace(modal.Name)
            ? "Alarm " + entityId.ToString(CultureInfo.InvariantCulture)
            : modal.Name.Trim();
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            await store.RenameAsync(Context.Guild.Id, serverId, entityId, name, CancellationToken.None)
                .ConfigureAwait(false);
        }

        await refresher.RefreshAsync(Context.Guild.Id, serverId, entityId, unreachable: false, CancellationToken.None)
            .ConfigureAwait(false);
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
