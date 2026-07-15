using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Wipes;

/// <summary>DI registration for the wipe-detection feature.</summary>
public static class WipeServiceCollectionExtensions
{
    /// <summary>Registers the wipe detector (announcer/renderer/poster/hosted service are added by later slices).</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddWipes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<IWipeDetector, WipeDetector>();
        services.AddSingleton<WipeEmbedRenderer>();
        services.AddSingleton<IWipeChannelPoster, DiscordWipeChannelPoster>();
        services.AddSingleton<IWipeAnnouncer, WipeAnnouncer>();

        return services;
    }
}
