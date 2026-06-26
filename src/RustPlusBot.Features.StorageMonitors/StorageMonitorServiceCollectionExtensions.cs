using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.StorageMonitors.Hosting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Relaying;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.StorageMonitors;

/// <summary>DI registration for the Smart Storage Monitors feature.</summary>
public static class StorageMonitorServiceCollectionExtensions
{
    /// <summary>Registers the item-name resolver, renderer, poster, coordinator, relay, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddStorageMonitors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddItemData();
        services.AddSingleton<StorageMonitorEmbedRenderer>();
        services.AddSingleton<IStorageMonitorChannelPoster, DiscordStorageMonitorChannelPoster>();
        services.AddSingleton<StorageMonitorPairingCoordinator>();
        services.AddSingleton<StorageMonitorStateRelay>();
        services.AddHostedService<StorageMonitorsHostedService>();

        services.AddSingleton(new InteractionModuleAssembly(
            typeof(StorageMonitorServiceCollectionExtensions).Assembly));

        return services;
    }
}
