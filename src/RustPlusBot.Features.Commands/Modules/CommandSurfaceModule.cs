using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>The /help, /uptime, and /leader slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class CommandSurfaceModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>The custom-id prefix for the /leader server-pick select.</summary>
    public const string LeaderServerSelectId = "leader-server";

    /// <summary>The custom-id prefix for the /leader member-pick select (suffixed with the server id).</summary>
    public const string LeaderPromotePrefix = "leader-promote:";

    /// <summary>Lists the available in-game and slash commands.</summary>
    [SlashCommand("help", "Show the available commands")]
    public async Task HelpAsync()
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
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var muteStore = scope.ServiceProvider.GetRequiredService<IMuteStore>();
            var renderer = scope.ServiceProvider.GetRequiredService<HelpEmbedRenderer>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var known = await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false);

            string prefix = "!";
            var showNote = false;
            if (known.Count == 1)
            {
                prefix = await muteStore.GetPrefixAsync(Context.Guild.Id, known[0].Id).ConfigureAwait(false);
            }
            else if (known.Count > 1)
            {
                showNote = true;
            }

            var embed = renderer.Render(prefix, culture, showNote);
            await FollowupAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }

    /// <summary>Reports the bot process uptime.</summary>
    [SlashCommand("uptime", "Show how long the bot has been running")]
    public async Task UptimeAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var localizer = scope.ServiceProvider.GetRequiredService<ICommandLocalizer>();
            var uptime = scope.ServiceProvider.GetRequiredService<BotUptime>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var text = localizer.Get("uptime.ok", culture, DurationFormat.Compact(uptime.Elapsed));
            await RespondAsync(text, ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>Transfers in-game team leadership (ManageGuild). Opens a server or member select.</summary>
    [SlashCommand("leader", "Transfer in-game team leadership")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task LeaderAsync()
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
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var leader = scope.ServiceProvider.GetRequiredService<LeaderService>();
            var localizer = scope.ServiceProvider.GetRequiredService<ICommandLocalizer>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var known = await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false);
            if (known.Count == 0)
            {
                await FollowupAsync(localizer.Get("leader.noserver", culture), ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (known.Count > 1)
            {
                var serverSelect = new SelectMenuBuilder()
                    .WithCustomId(LeaderServerSelectId)
                    .WithPlaceholder(localizer.Get("leader.pickserver", culture));
                foreach (var server in known.Take(SelectMenuBuilder.MaxOptionCount))
                {
                    serverSelect.AddOption(server.Name, server.Id.ToString());
                }

                var components = new ComponentBuilder().WithSelectMenu(serverSelect).Build();
                await FollowupAsync(localizer.Get("leader.pickserver", culture), ephemeral: true,
                    components: components).ConfigureAwait(false);
                return;
            }

            await ShowMemberSelectAsync(scope.ServiceProvider, leader, localizer, Context.Guild.Id, known[0].Id,
                culture).ConfigureAwait(false);
        }
    }

    private async Task ShowMemberSelectAsync(
        IServiceProvider provider,
        LeaderService leader,
        ICommandLocalizer localizer,
        ulong guildId,
        Guid serverId,
        string culture)
    {
        _ = provider;
        var result = await leader.GetMembersAsync(guildId, serverId, culture, CancellationToken.None)
            .ConfigureAwait(false);
        if (result.ErrorMessage is { } error)
        {
            await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var select = new SelectMenuBuilder()
            .WithCustomId(string.Create(CultureInfo.InvariantCulture, $"{LeaderPromotePrefix}{serverId}"))
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
