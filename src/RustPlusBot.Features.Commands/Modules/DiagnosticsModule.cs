using System.Diagnostics;
using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Read-only diagnostics: /ping and /status.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class DiagnosticsModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Reports Discord gateway latency and the REST ack round-trip.</summary>
    [SlashCommand("ping", "Show the bot's Discord latency")]
    public async Task PingAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        stopwatch.Stop();

        var text = string.Create(CultureInfo.InvariantCulture,
            $"Pong! Gateway: {Context.Client.Latency} ms · Response: {stopwatch.ElapsedMilliseconds} ms");
        await FollowupAsync(text, ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Shows uptime, latency, per-server connection status, and counts.</summary>
    [SlashCommand("status", "Show bot health and connection status")]
    public async Task StatusAsync()
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
            var uptime = scope.ServiceProvider.GetRequiredService<BotUptime>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var connections = scope.ServiceProvider.GetRequiredService<IConnectionStore>();

            var known = await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false);
            var states = (await connections.GetStatesForGuildAsync(Context.Guild.Id).ConfigureAwait(false))
                .ToDictionary(state => state.RustServerId);

            var embed = new EmbedBuilder()
                .WithTitle("Bot status")
                .AddField("Uptime", DurationFormat.Compact(uptime.Elapsed), inline: true)
                .AddField("Gateway latency",
                    string.Create(CultureInfo.InvariantCulture, $"{Context.Client.Latency} ms"), inline: true)
                .AddField("Guilds",
                    Context.Client.Guilds.Count.ToString(CultureInfo.InvariantCulture), inline: true)
                .AddField("Servers (this guild)",
                    known.Count.ToString(CultureInfo.InvariantCulture), inline: true);

            // Cap server fields so the embed stays under Discord's 25-field limit (4 header fields above).
            foreach (var server in known.Take(20))
            {
                var line = states.TryGetValue(server.Id, out var state)
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"{state.Status} · {state.PlayerCount?.ToString(CultureInfo.InvariantCulture) ?? "?"} players")
                    : "unknown";
                embed.AddField(server.Name, line);
            }

            await FollowupAsync(ephemeral: true, embed: embed.Build()).ConfigureAwait(false);
        }
    }
}
