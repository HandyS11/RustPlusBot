using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Provisions the bot's Discord workspace for the current guild.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class SetupModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Reconciles the global workspace and every known server's workspace.</summary>
    [SlashCommand("setup", "Provision the bot's channels for this server")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SetupAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            var result = await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);
            if (result.Status == ReconcileStatus.MissingPermissions)
            {
                await FollowupAsync(
                    $"I'm missing required permissions: {string.Join(", ", result.MissingPermissions)}.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            foreach (var server in await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false))
            {
                await reconciler.ReconcileServerAsync(Context.Guild.Id, server.Id).ConfigureAwait(false);
            }

            await FollowupAsync("Workspace is ready.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
