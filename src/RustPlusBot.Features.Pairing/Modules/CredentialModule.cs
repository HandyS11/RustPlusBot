using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Features.Pairing.Validation;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Features.Pairing.Modules;

/// <summary>Handles the #setup "Connect account" button and the credentials modal submission.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class CredentialModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Opens the credentials modal when the #setup button is clicked.</summary>
    [ComponentInteraction(WorkspaceComponentIds.ConnectAccount)]
    public async Task OpenAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondWithModalAsync<ConnectModal>(ConnectModal.ModalId).ConfigureAwait(false);
    }

    /// <summary>Validates, stores, and connects the submitted credentials.</summary>
    /// <param name="modal">The submitted modal.</param>
    [ModalInteraction(ConnectModal.ModalId)]
    public async Task SubmitAsync(ConnectModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (!FcmCredentialValidator.IsWellFormed(modal.Credentials))
        {
            await FollowupAsync(
                "I couldn't read those credentials. Paste the full JSON produced by the credentials helper.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var registrations = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            await registrations.UpsertAsync(Context.Guild.Id, Context.User.Id, modal.Credentials).ConfigureAwait(false);

            var supervisor = scope.ServiceProvider.GetRequiredService<IPairingSupervisor>();
            var outcome = await supervisor.EnsureListenerAsync(Context.Guild.Id, Context.User.Id).ConfigureAwait(false);

            var message = outcome switch
            {
                PairingConnectOutcome.Connected =>
                    "Account connected. Pair with a server in-game and it'll show up here automatically.",
                PairingConnectOutcome.Rejected =>
                    "Those credentials were rejected by Rust+. Generate fresh credentials and reconnect.",
                _ => "Account saved — still verifying the connection. Pairings will appear once it's up.",
            };
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }
    }
}
