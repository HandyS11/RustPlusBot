using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RustPlusBot.Discord;

/// <summary>
/// Hosted service that logs the bot in, loads interaction modules, and registers slash commands
/// per guild on ready/join (per-guild registration is instant, unlike global commands).
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="interactions">The interaction service that dispatches slash commands.</param>
/// <param name="services">The root service provider used to construct interaction modules.</param>
/// <param name="moduleAssemblies">Additional assemblies to scan for interaction modules contributed by feature projects.</param>
/// <param name="options">The Discord options carrying the bot token.</param>
/// <param name="logger">The logger.</param>
[SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging",
    Justification =
        "Log-bridge arguments are cheap LogMessage property reads; an IsEnabled guard would be redundant noise.")]
public sealed class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    IEnumerable<InteractionModuleAssembly> moduleAssemblies,
    IOptions<DiscordOptions> options,
    ILogger<DiscordBotService> logger) : IHostedService
{
    private readonly DiscordOptions _options = options.Value;
    private bool _hasRegisteredCommands;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += OnLogAsync;
        interactions.Log += OnLogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;
        client.JoinedGuild += OnJoinedGuildAsync;

        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services).ConfigureAwait(false);
        foreach (var moduleAssembly in moduleAssemblies)
        {
            await interactions.AddModulesAsync(moduleAssembly.Assembly, services).ConfigureAwait(false);
        }

        await client.LoginAsync(TokenType.Bot, _options.Token).ConfigureAwait(false);
        await client.StartAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.LogoutAsync().ConfigureAwait(false);
        await client.StopAsync().ConfigureAwait(false);
    }

    private async Task OnReadyAsync()
    {
        // Ready fires on every gateway (re)connect; only register commands once per process.
        // Ready is dispatched serially on the gateway thread, so no synchronization is needed.
        if (_hasRegisteredCommands)
        {
            return;
        }

        foreach (var guild in client.Guilds)
        {
            await interactions.RegisterCommandsToGuildAsync(guild.Id).ConfigureAwait(false);
        }

        _hasRegisteredCommands = true;
        logger.LogInformation("Registered commands to {GuildCount} guild(s).", client.Guilds.Count);
    }

    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification =
            "Method signature is constrained by the Discord.Net JoinedGuild event delegate (Func<SocketGuild, Task>).")]
    private Task OnJoinedGuildAsync(SocketGuild guild) =>
        interactions.RegisterCommandsToGuildAsync(guild.Id);

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(client, interaction);
        await interactions.ExecuteCommandAsync(context, services).ConfigureAwait(false);
    }

    private Task OnLogAsync(LogMessage message)
    {
        logger.Log(ToLogLevel(message.Severity), message.Exception, "[{Source}] {Message}", message.Source,
            message.Message);
        return Task.CompletedTask;
    }

    private static LogLevel ToLogLevel(LogSeverity severity) => severity switch
    {
        LogSeverity.Critical => LogLevel.Critical,
        LogSeverity.Error => LogLevel.Error,
        LogSeverity.Warning => LogLevel.Warning,
        LogSeverity.Info => LogLevel.Information,
        LogSeverity.Verbose => LogLevel.Debug,
        LogSeverity.Debug => LogLevel.Trace,
        _ => LogLevel.Information,
    };
}
