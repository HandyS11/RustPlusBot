using System.Globalization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Handles the /leader server-pick and member-pick select callbacks.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class LeaderComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Handles the server-pick step (multi-server guilds): re-shows a member select for the chosen server.</summary>
    /// <param name="values">The selected server id (one value).</param>
    [ComponentInteraction(CommandSurfaceModule.LeaderServerSelectId, ignoreGroupNames: true)]
    public async Task OnServerSelectedAsync(string[] values)
    {
        if (!await EnsureManageGuildAsync().ConfigureAwait(false) ||
            values is not [var raw] || !Guid.TryParse(raw, out var serverId))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var leader = scope.ServiceProvider.GetRequiredService<LeaderService>();
            var localizer = scope.ServiceProvider.GetRequiredService<ICommandLocalizer>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var result = await leader.GetMembersAsync(Context.Guild.Id, serverId, culture, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.ErrorMessage is { } error)
            {
                await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
                return;
            }

            var select = new SelectMenuBuilder()
                .WithCustomId(string.Create(CultureInfo.InvariantCulture,
                    $"{CommandSurfaceModule.LeaderPromotePrefix}{serverId}"))
                .WithPlaceholder(localizer.Get("leader.pickmember", culture));
            foreach (var member in result.Members.Take(SelectMenuBuilder.MaxOptionCount))
            {
                select.AddOption(member.Name, member.SteamId.ToString(CultureInfo.InvariantCulture),
                    isDefault: member.IsLeader);
            }

            var components = new ComponentBuilder().WithSelectMenu(select).Build();
            await FollowupAsync(localizer.Get("leader.pickmember", culture), ephemeral: true, components: components)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Handles the member-pick step: promotes the chosen member.</summary>
    /// <param name="serverIdRaw">The server id from the wildcard custom-id.</param>
    /// <param name="values">The selected member's Steam id (one value).</param>
    [ComponentInteraction($"{CommandSurfaceModule.LeaderPromotePrefix}*", ignoreGroupNames: true)]
    public async Task OnMemberSelectedAsync(string serverIdRaw, string[] values)
    {
        if (!await EnsureManageGuildAsync().ConfigureAwait(false) ||
            !Guid.TryParse(serverIdRaw, out var serverId) ||
            values is not [var rawSteamId] ||
            !ulong.TryParse(rawSteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var leader = scope.ServiceProvider.GetRequiredService<LeaderService>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);

            // Re-resolve the member name for the success message; fall back to the SteamId.
            var team = await leader.GetMembersAsync(Context.Guild.Id, serverId, culture, CancellationToken.None)
                .ConfigureAwait(false);
            var name = team.Members.FirstOrDefault(m => m.SteamId == steamId)?.Name
                       ?? steamId.ToString(CultureInfo.InvariantCulture);

            var message = await leader
                .PromoteAsync(Context.Guild.Id, serverId, steamId, name, culture, CancellationToken.None)
                .ConfigureAwait(false);
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureManageGuildAsync()
    {
        // Discord does not re-gate component callbacks, so re-check ManageGuild here.
        if (Context.User is SocketGuildUser user && user.GuildPermissions.ManageGuild)
        {
            return true;
        }

        await RespondAsync("You need the Manage Server permission.", ephemeral: true).ConfigureAwait(false);
        return false;
    }
}
