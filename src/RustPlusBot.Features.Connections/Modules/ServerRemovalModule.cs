using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Connections.Removal;
using RustPlusBot.Features.Workspace;

namespace RustPlusBot.Features.Connections.Modules;

/// <summary>Handles the #info "Remove server" button and its confirmation (ManageGuild).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ServerRemovalModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string RemoveConfirmPrefix = "connections:server:remove:confirm:";
    private const string RemoveCancelId = "connections:server:remove:cancel";

    /// <summary>Opens an ephemeral confirmation for removing the server.</summary>
    /// <param name="serverIdRaw">The server id captured from the custom id.</param>
    [ComponentInteraction(WorkspaceComponentIds.ServerInfoRemovePrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task RemovePromptAsync(string serverIdRaw)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(serverIdRaw, out var serverId))
        {
            await RespondAsync("That selection wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Remove server", $"{RemoveConfirmPrefix}{serverId}", ButtonStyle.Danger)
            .WithButton("Cancel", RemoveCancelId, ButtonStyle.Secondary)
            .Build();
        await RespondAsync(
                "This permanently deletes the category, all channels, and stored credentials for this server. Continue?",
                ephemeral: true, components: components)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the server after confirmation.</summary>
    /// <param name="serverIdRaw">The server id captured from the custom id.</param>
    [ComponentInteraction(RemoveConfirmPrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task RemoveConfirmAsync(string serverIdRaw)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(serverIdRaw, out var serverId))
        {
            await RespondAsync("That selection wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var removal = scope.ServiceProvider.GetRequiredService<IServerRemovalService>();
            var removed = await removal.RemoveServerAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            await FollowupAsync(removed ? "Server removed." : "That server no longer exists.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Cancels the removal.</summary>
    [ComponentInteraction(RemoveCancelId)]
    public async Task RemoveCancelAsync() =>
        await RespondAsync("Cancelled.", ephemeral: true).ConfigureAwait(false);
}
