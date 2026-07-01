using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RustPlusBot.Persistence.Maintenance;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Dangerous, danger-gated bot maintenance commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="options">Gates the command behind the danger flag.</param>
[Group("admin", "Bot maintenance (dangerous)")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class MaintenanceModule(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceOptions> options) : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Wipes all bot data across all guilds after a typed confirmation.</summary>
    /// <param name="confirm">Must equal the literal "RESET" to proceed.</param>
    [SlashCommand("reset-database", "Wipe ALL bot data across ALL servers (dangerous)")]
    public async Task ResetDatabaseAsync(
        [Summary("confirm", "Type RESET to confirm")]
        string confirm)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!options.Value.EnableDangerCommands)
        {
            await RespondAsync(
                "Dangerous maintenance commands are disabled. Set `Workspace:EnableDangerCommands` to enable them.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(confirm, "RESET", StringComparison.Ordinal))
        {
            await RespondAsync("Type `RESET` exactly to confirm the database wipe.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();
            await maintenance.ClearAllAsync().ConfigureAwait(false);
            await FollowupAsync("Database cleared. **Restart the bot** for a clean state.", ephemeral: true)
                .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
