using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Features.Commands.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Read-only live-data slash commands (/pop, /time, /wipe, /online, /offline, /team, /alive).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ServerCommandModule(
    IServiceScopeFactory scopeFactory,
    ILogger<ServerCommandModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Shows the current player count.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("pop", "Show the current player count")]
    public Task PopAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("pop", server);

    /// <summary>Shows the in-game time.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("time", "Show the in-game time")]
    public Task TimeAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("time", server);

    /// <summary>Shows how long ago the server wiped.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("wipe", "Show how long ago the server wiped")]
    public Task WipeAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("wipe", server);

    /// <summary>Lists online teammates.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("online", "List online teammates")]
    public Task OnlineAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("online", server);

    /// <summary>Lists offline teammates.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("offline", "List offline teammates")]
    public Task OfflineAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("offline", server);

    /// <summary>Lists all team members.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("team", "List all team members")]
    public Task TeamAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("team", server);

    /// <summary>Shows how long each teammate has survived.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("alive", "Show how long each teammate has survived")]
    public Task AliveAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("alive", server);

    private async Task RunAsync(string commandName, string? server)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();
                var query = scope.ServiceProvider.GetRequiredService<ServerQueryService>();

                var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
                var resolution = await resolver.ResolveAsync(Context.Guild.Id, server, culture, CancellationToken.None)
                    .ConfigureAwait(false);
                if (resolution.ErrorMessage is { } error)
                {
                    await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var reply = await query.RunAsync(commandName, Context.Guild.Id, resolution.ServerId!.Value, culture,
                    CancellationToken.None).ConfigureAwait(false);
                await FollowupAsync(reply ?? "Something went wrong.", ephemeral: true).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Broad catch: an interaction failure must not surface as a Discord "interaction failed".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCommandFaulted(logger, ex, commandName);
            await FollowupAsync("Something went wrong.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Slash data command '{Command}' faulted.")]
    private static partial void LogCommandFaulted(ILogger logger, Exception exception, string command);
}
