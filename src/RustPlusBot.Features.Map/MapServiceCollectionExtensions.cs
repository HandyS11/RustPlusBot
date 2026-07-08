using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map;

/// <summary>DI registration for the map-render feature.</summary>
public static class MapServiceCollectionExtensions
{
    /// <summary>Registers the renderer, base-map cache, composer, poster, pipeline bundle, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddMap(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MapRenderer>();
        services.AddSingleton<IBaseMapSource, RustPlusBaseMapSource>();
        services.AddSingleton<BaseMapCache>();
        services.AddSingleton<MapComposer>();
        services.AddSingleton<IMapChannelPoster, DiscordMapChannelPoster>();
        services.AddSingleton<MapPipeline>();
        services.AddHostedService<MapHostedService>();

        return services;
    }
}
