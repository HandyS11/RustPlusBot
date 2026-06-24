using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Players.Hosting;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Players;

/// <summary>DI registration for the team-presence-events feature.</summary>
public static class PlayerEventServiceCollectionExtensions
{
    /// <summary>Registers the localizer, renderer, poster, relay, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPlayers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<PlayerEventRenderer>();
        services.AddSingleton<IPlayerChannelPoster, DiscordPlayerChannelPoster>();
        services.AddSingleton<PlayerEventRelay>();
        services.AddHostedService<PlayersHostedService>();

        return services;
    }
}
