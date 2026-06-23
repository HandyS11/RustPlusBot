using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Alarms.Hosting;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Alarms.Rendering;

namespace RustPlusBot.Features.Alarms;

/// <summary>DI registration for the Smart Alarms feature.</summary>
public static class AlarmServiceCollectionExtensions
{
    /// <summary>Registers the localizer, renderer, poster, refresher, coordinator, relay, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAlarms(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAlarmLocalizer>(new AlarmLocalizer(AlarmLocalizationCatalog.Default));
        services.AddSingleton<AlarmEmbedRenderer>();
        services.AddSingleton<IAlarmChannelPoster, DiscordAlarmChannelPoster>();
        services.AddSingleton<IAlarmRefresher, AlarmRefresher>();
        services.AddSingleton<AlarmPairingCoordinator>();
        services.AddSingleton<AlarmRelayChannels>();
        services.AddSingleton<AlarmStateRelay>();
        services.AddHostedService<AlarmsHostedService>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(AlarmServiceCollectionExtensions).Assembly));

        return services;
    }
}
