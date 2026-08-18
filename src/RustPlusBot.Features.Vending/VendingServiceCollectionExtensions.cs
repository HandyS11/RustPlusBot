using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Discord;
using RustPlusBot.Features.Vending.Hosting;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Posting;
using RustPlusBot.Features.Vending.Relaying;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Features.Vending.Tracking;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending;

/// <summary>DI registration for the vending feature.</summary>
public static class VendingServiceCollectionExtensions
{
    /// <summary>Registers the index, read model, track service, renderer, poster, relay, purger, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddVending(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<VendingIndex>();
        services.AddSingleton<IVendingReadModel>(sp => sp.GetRequiredService<VendingIndex>());
        services.AddScoped<IVendingTrackService, VendingTrackService>();
        services.AddSingleton<VendingEmbedRenderer>();
        services.AddSingleton<IVendingChannelPoster, DiscordVendingChannelPoster>();
        services.AddSingleton<VendingNotificationRelay>();
        services.AddSingleton<VendingWipePurger>();
        services.AddHostedService<VendingHostedService>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(VendingServiceCollectionExtensions).Assembly));

        return services;
    }
}
