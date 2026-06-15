using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord.Notifications;

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
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = false,
        };

        services.AddSingleton(socketConfig);
        services.AddSingleton<DiscordSocketClient>();
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig
            {
                DefaultRunMode = RunMode.Async
            }));
        services.AddSingleton<IUserDmSender, DiscordUserDmSender>();
        services.AddHostedService<DiscordBotService>();

        return services;
    }
}
