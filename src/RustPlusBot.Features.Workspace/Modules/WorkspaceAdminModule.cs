using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Administrative and developer commands for the workspace.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="options">Workspace options (gates the dangerous commands).</param>
/// <param name="eventBus">Used to publish the stub <see cref="ServerRegisteredEvent"/>.</param>
[Group("workspace", "Workspace administration")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class WorkspaceAdminModule(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkspaceOptions> options,
    IEventBus eventBus) : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Custom id for the reset confirmation button.</summary>
    public const string ConfirmResetId = "workspace:reset:confirm";

    /// <summary>Prompts to delete the entire provisioned workspace (dev-gated).</summary>
    [SlashCommand("reset", "Delete ALL provisioned channels and records for this server (dangerous)")]
    public async Task ResetAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Confirm reset", ConfirmResetId, ButtonStyle.Danger)
            .Build();
        await RespondAsync(
            "This deletes every channel and category the bot provisioned here. Confirm?",
            ephemeral: true, components: components).ConfigureAwait(false);
    }

    /// <summary>Executes the workspace reset after confirmation.</summary>
    [ComponentInteraction(ConfirmResetId)]
    public async Task ConfirmResetAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var teardown = scope.ServiceProvider.GetRequiredService<IWorkspaceTeardownService>();
            await teardown.ResetGuildAsync(Context.Guild.Id).ConfigureAwait(false);
            await FollowupAsync("Workspace reset.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Dev: registers a fake server and publishes a ServerRegisteredEvent to test provisioning.</summary>
    /// <param name="name">Server display name.</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    [SlashCommand("simulate-server", "Dev: register a fake server to test provisioning")]
    public async Task SimulateServerAsync(string name, string ip, int port)
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var server = await servers.AddAsync(Context.Guild.Id, Context.User.Id, name, ip, port).ConfigureAwait(false);
            await eventBus.PublishAsync(new ServerRegisteredEvent(Context.Guild.Id, server.Id)).ConfigureAwait(false);
            await FollowupAsync($"Registered **{name}** and published ServerRegisteredEvent.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureEnabledAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        if (!options.Value.EnableDangerCommands)
        {
            await RespondAsync("Developer commands are disabled.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        return true;
    }
}
