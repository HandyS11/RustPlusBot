using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Connections.Servers;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Connections.Modules;

/// <summary>
///     /server player — replaces the "switch active player" select that used to sit on the #info
///     embed. It replies ephemerally with that same select (same custom id), so
///     <see cref="ConnectionComponentModule" /> handles the choice with no new interaction logic.
/// </summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
[Group("server", "Server administration")]
public sealed class ServerPlayerModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Shows the credential picker for a server.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    /// <returns>A task that completes when the reply has been sent.</returns>
    [SlashCommand("player", "Switch which paired player drives a server's connection")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task PlayerAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();
            var localizer = scope.ServiceProvider.GetRequiredService<ILocalizer>();
            var connections = scope.ServiceProvider.GetRequiredService<IConnectionStore>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var resolution = await resolver
                .ResolveAsync(Context.Guild.Id, server, culture, CancellationToken.None).ConfigureAwait(false);
            if (resolution.ErrorMessage is { } error)
            {
                await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
                return;
            }

            var serverId = resolution.ServerId!.Value;
            var state = await connections.GetStateAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            var pool = await connections.ListPoolAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            var eligible = pool
                .Where(c => c.Status != CredentialStatus.Invalid)
                .Take(SelectMenuBuilder.MaxOptionCount) // Discord hard-limits a select menu to 25 options.
                .ToList();
            if (eligible.Count == 0)
            {
                await FollowupAsync(localizer.Get("command.server.player.nopool", culture), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var select = new SelectMenuBuilder()
                .WithCustomId($"{WorkspaceComponentIds.ServerInfoSwapPrefix}{serverId}")
                .WithPlaceholder(localizer.Get("server.info.swap.placeholder", culture));
            foreach (var credential in eligible)
            {
                select.AddOption(
                    credential.SteamId.ToString(CultureInfo.InvariantCulture),
                    credential.Id.ToString(),
                    isDefault: credential.Id == state?.ActiveCredentialId);
            }

            await FollowupAsync(
                    localizer.Get("command.server.player.pick", culture),
                    ephemeral: true,
                    components: new ComponentBuilder().WithSelectMenu(select).Build())
                .ConfigureAwait(false);
        }
    }
}
