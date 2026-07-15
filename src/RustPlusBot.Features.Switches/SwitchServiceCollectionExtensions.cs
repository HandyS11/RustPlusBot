using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Switches.Hosting;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Relaying;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches;

/// <summary>DI registration for the Smart Switches feature.</summary>
public static class SwitchServiceCollectionExtensions
{
    /// <summary>Registers the localizer, renderer, poster, coordinator, relay, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSwitches(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<SwitchEmbedRenderer>();
        services.AddSingleton<ISwitchChannelPoster, DiscordSwitchChannelPoster>();
        services.AddSingleton<SwitchPairingCoordinator>();
        services.AddSingleton<SwitchStateRelay>();
        services.AddSingleton<SwitchWipePurger>();
        services.AddHostedService<SwitchesHostedService>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(SwitchServiceCollectionExtensions).Assembly));

        return services;
    }
}
