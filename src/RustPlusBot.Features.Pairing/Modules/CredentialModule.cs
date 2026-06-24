using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Pairing.Accounts;
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
    private const string ServerOnlyMessage = "This control must be used in a server.";
    private const string DisconnectConfirmId = "pairing:account:disconnect:confirm";
    private const string DisconnectCancelId = "pairing:account:disconnect:cancel";

    /// <summary>Opens the credentials modal when the #setup button is clicked.</summary>
    [ComponentInteraction(WorkspaceComponentIds.ConnectAccount)]
    public async Task OpenAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ServerOnlyMessage, ephemeral: true).ConfigureAwait(false);
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
            await RespondAsync(ServerOnlyMessage, ephemeral: true).ConfigureAwait(false);
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

    /// <summary>Opens an ephemeral confirmation listing the servers a disconnect would affect.</summary>
    [ComponentInteraction(WorkspaceComponentIds.DisconnectAccount)]
    public async Task DisconnectPromptAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ServerOnlyMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var disconnect = scope.ServiceProvider.GetRequiredService<IAccountDisconnectService>();
            var preview = await disconnect.PreviewAsync(Context.Guild.Id, Context.User.Id).ConfigureAwait(false);
            if (!preview.IsConnected)
            {
                await FollowupAsync("You're not connected.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var servers = preview.AffectedServerNames.Count > 0
                ? string.Join(", ", preview.AffectedServerNames)
                : "no servers yet";
            var components = new ComponentBuilder()
                .WithButton("Disconnect", DisconnectConfirmId, ButtonStyle.Danger)
                .WithButton("Cancel", DisconnectCancelId, ButtonStyle.Secondary)
                .Build();
            await FollowupAsync(
                    $"This disconnects your account and removes your credentials from: {servers}. Continue?",
                    ephemeral: true, components: components)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Performs the disconnect after confirmation.</summary>
    [ComponentInteraction(DisconnectConfirmId)]
    public async Task DisconnectConfirmAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ServerOnlyMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var disconnect = scope.ServiceProvider.GetRequiredService<IAccountDisconnectService>();
            var count = await disconnect.DisconnectAsync(Context.Guild.Id, Context.User.Id).ConfigureAwait(false);
            await FollowupAsync($"Account disconnected. Removed from {count} server(s).", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Cancels the disconnect.</summary>
    [ComponentInteraction(DisconnectCancelId)]
    public async Task DisconnectCancelAsync() =>
        await RespondAsync("Cancelled.", ephemeral: true).ConfigureAwait(false);
}
