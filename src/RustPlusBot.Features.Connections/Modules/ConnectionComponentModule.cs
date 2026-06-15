using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Connections.Modules;

/// <summary>Handles the #info "switch active player" select (ManageGuild).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ConnectionComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Promotes the chosen pooled credential to active and restarts the socket on it.</summary>
    /// <param name="serverIdRaw">The server id captured from the custom id.</param>
    /// <param name="selectedValues">The selected credential id (expects exactly one).</param>
    // Custom id: WorkspaceComponentIds.ServerInfoSwapPrefix + "*" = "workspace:info:swap:*"
    [ComponentInteraction(WorkspaceComponentIds.ServerInfoSwapPrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SwapAsync(string serverIdRaw, string[] selectedValues)
    {
        ArgumentNullException.ThrowIfNull(selectedValues);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(serverIdRaw, out var serverId)
            || selectedValues.Length == 0
            || !Guid.TryParse(selectedValues[0], out var credentialId))
        {
            await RespondAsync("That selection wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            if (!await store.PromoteAsync(Context.Guild.Id, serverId, credentialId).ConfigureAwait(false))
            {
                await FollowupAsync("That player isn't available to activate.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var supervisor = scope.ServiceProvider.GetRequiredService<IConnectionSupervisor>();
            await supervisor.EnsureConnectionAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            await FollowupAsync("Switching active player…", ephemeral: true).ConfigureAwait(false);
        }
    }
}
