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

    /// <summary>Custom id for the rebuild confirmation button.</summary>
    public const string ConfirmRebuildId = "workspace:rebuild:confirm";

    /// <summary>Custom id for the purge confirmation button.</summary>
    public const string ConfirmPurgeId = "workspace:purge:confirm";

    /// <summary>Prompts to delete the entire provisioned workspace (dev-gated).</summary>
    [SlashCommand("reset", "Delete ALL of the bot's channels and records in this Discord server (dangerous)")]
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

        if (port is < 1 or > 65535)
        {
            await RespondAsync("Port must be between 1 and 65535.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var server = await servers.AddAsync(Context.Guild.Id, Context.User.Id, name, ip, port)
                .ConfigureAwait(false);
            await eventBus.PublishAsync(new ServerRegisteredEvent(Context.Guild.Id, server.Id)).ConfigureAwait(false);
            await FollowupAsync($"Registered **{name}** and published ServerRegisteredEvent.", ephemeral: true)
                .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Recreates any missing categories/channels/messages without deleting data.</summary>
    [SlashCommand("repair", "Recreate any missing bot channels without deleting data")]
    public async Task RepairAsync()
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
            await reconciler.HealGuildAsync(Context.Guild.Id).ConfigureAwait(false);
            await FollowupAsync("Workspace repaired.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Prompts to delete and re-provision the entire workspace (dev-gated).</summary>
    [SlashCommand("rebuild", "Delete and re-create all the bot's channels here (dangerous)")]
    public async Task RebuildAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Confirm rebuild", ConfirmRebuildId, ButtonStyle.Danger)
            .Build();
        await RespondAsync(
            "This deletes every provisioned channel and re-creates them from scratch. Confirm?",
            ephemeral: true, components: components).ConfigureAwait(false);
    }

    /// <summary>Executes the rebuild after confirmation.</summary>
    [ComponentInteraction(ConfirmRebuildId)]
    public async Task ConfirmRebuildAsync()
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
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();

            await teardown.ResetGuildAsync(Context.Guild.Id).ConfigureAwait(false);

            var result = await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);
            if (result.Status == ReconcileStatus.MissingPermissions)
            {
                await FollowupAsync(
                    $"I'm missing required permissions: {string.Join(", ", result.MissingPermissions)}.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            foreach (var server in await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false))
            {
                await reconciler.ReconcileServerAsync(Context.Guild.Id, server.Id).ConfigureAwait(false);
            }

            await FollowupAsync("Workspace rebuilt.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Prompts to delete all of this guild's data (dev-gated).</summary>
    [SlashCommand("purge", "Delete ALL of this server's bot data (servers, settings, channels) (dangerous)")]
    public async Task PurgeAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Confirm purge", ConfirmPurgeId, ButtonStyle.Danger)
            .Build();
        await RespondAsync(
            "This deletes every server, setting, and channel the bot stores for this Discord server. Confirm?",
            ephemeral: true, components: components).ConfigureAwait(false);
    }

    /// <summary>Executes the purge after confirmation.</summary>
    [ComponentInteraction(ConfirmPurgeId)]
    public async Task ConfirmPurgeAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var purge = scope.ServiceProvider.GetRequiredService<IGuildPurgeService>();
            await purge.PurgeGuildAsync(Context.Guild.Id).ConfigureAwait(false);
            await FollowupAsync("Guild data purged.", ephemeral: true).ConfigureAwait(false);
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
