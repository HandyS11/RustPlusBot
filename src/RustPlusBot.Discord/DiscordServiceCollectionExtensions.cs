using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace RustPlusBot.Discord;

/// <summary>DI registration for the Discord layer.</summary>
public static class DiscordServiceCollectionExtensions
{
    /// <summary>Registers the socket client, interaction service, and the hosted bot service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDiscordBot(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            AlwaysDownloadUsers = false,
        };

        services.AddSingleton(socketConfig);
        services.AddSingleton<DiscordSocketClient>();
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Async }));
        services.AddHostedService<DiscordBotService>();

        return services;
    }
}
