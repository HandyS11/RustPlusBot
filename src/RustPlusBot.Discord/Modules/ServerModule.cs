using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Discord.Modules;

/// <summary>Guild-scoped management of Rust+ servers via slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per command.</param>
[Group("server", "Manage this guild's Rust+ servers")]
public sealed class ServerModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Adds a Rust+ server to this guild.</summary>
    /// <param name="name">Display name.</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    [SlashCommand("add", "Add a Rust+ server to this guild")]
    public async Task AddAsync(
        [Summary("name", "Display name")] string name,
        [Summary("ip", "Server host or ip")] string ip,
        [Summary("port", "Rust+ app port")] int port)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<IServerService>();
            var server = await service.AddAsync(Context.Guild.Id, Context.User.Id, name, ip, port).ConfigureAwait(false);
            await RespondAsync($"Added **{server.Name}** (`{server.Id}`).", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Lists this guild's Rust+ servers.</summary>
    [SlashCommand("list", "List this guild's Rust+ servers")]
    public async Task ListAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<IServerService>();
            var servers = await service.ListAsync(Context.Guild.Id).ConfigureAwait(false);

            if (servers.Count == 0)
            {
                await RespondAsync("No servers configured.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var builder = new StringBuilder();
            foreach (var server in servers)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"• **{server.Name}** — `{server.Ip}:{server.Port}` (`{server.Id}`)");
            }

            await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Removes a Rust+ server by id.</summary>
    /// <param name="id">The server id from <c>/server list</c>.</param>
    [SlashCommand("remove", "Remove a Rust+ server by id")]
    public async Task RemoveAsync([Summary("id", "The server id from /server list")] string id)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(id, out var serverId))
        {
            await RespondAsync("That is not a valid server id.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<IServerService>();
            var removed = await service.RemoveAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            await RespondAsync(removed ? "Removed." : "No matching server found.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
