using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Pairing.Accounts;
using RustPlusBot.Features.Pairing.Hosting;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Posting;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Pairing;

/// <summary>DI registration for the pairing feature.</summary>
public static class PairingServiceCollectionExtensions
{
    /// <summary>Registers the pairing source, supervisor, handler, notifier, module seam, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPairing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPairingHandler, PairingHandler>();
        services.AddSingleton<IPairingSource, RustPlusFcmPairingSource>();
        services.AddSingleton<IOwnerNotifier, DiscordOwnerNotifier>();
        services.AddSingleton<IPairingSupervisor, PairingSupervisor>();
        services.AddScoped<IAccountDisconnectService, AccountDisconnectService>();

        services.AddRustPlusBotLocalization();
        services.AddSingleton<ServerPairingPromptRenderer>();
        services.AddSingleton<ISetupChannelPoster, DiscordSetupChannelPoster>();
        services.AddSingleton<IServerPairingCoordinator, ServerPairingCoordinator>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(PairingServiceCollectionExtensions).Assembly));

        services.AddHostedService<PairingHostedService>();

        return services;
    }
}
