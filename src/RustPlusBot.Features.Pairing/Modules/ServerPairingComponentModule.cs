using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Rendering;

namespace RustPlusBot.Features.Pairing.Modules;

/// <summary>Thin handler for the #setup server-pairing prompt buttons. Any guild member.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ServerPairingComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";
    private const string ExpiredMessage = "This pairing expired — press \"Pair\" in-game again.";

    /// <summary>Accepts a pending server pairing and triggers the full registration cycle.</summary>
    /// <param name="tail">The "{ip}:{port}" custom-id tail.</param>
    [ComponentInteraction(ServerPairingComponentIds.AcceptPrefix + "*")]
    public async Task AcceptAsync(string tail)
    {
        if (!ServerPairingComponentIds.TryParseTail(tail, out var ip, out var port) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        ServerPairingAcceptOutcome outcome;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<IServerPairingCoordinator>();
            outcome = await coordinator.TryAcceptAsync(Context.Guild.Id, ip, port, CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (outcome == ServerPairingAcceptOutcome.Expired)
        {
            // The prompt outlived its pending state (restart/double-click) — clean up the orphaned message.
            await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        }

        await FollowupAsync(outcome switch
        {
            ServerPairingAcceptOutcome.Added => "Server added.",
            ServerPairingAcceptOutcome.AlreadyAdded => "That server was already added.",
            _ => ExpiredMessage,
        }, ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Dismisses a pending server pairing and removes the transient prompt message.</summary>
    /// <param name="tail">The "{ip}:{port}" custom-id tail.</param>
    [ComponentInteraction(ServerPairingComponentIds.DismissPrefix + "*")]
    public async Task DismissAsync(string tail)
    {
        if (!ServerPairingComponentIds.TryParseTail(tail, out var ip, out var port) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        bool dismissed;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<IServerPairingCoordinator>();
            dismissed = coordinator.TryDismiss(Context.Guild.Id, ip, port);
        }

        // Delete the actual prompt message that hosts this button (the component interaction's source
        // message) — not the ephemeral interaction response. Best-effort: a delete failure is non-fatal.
        await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        await RespondAsync(dismissed ? "Dismissed." : ExpiredMessage, ephemeral: true).ConfigureAwait(false);
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
