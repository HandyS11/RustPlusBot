using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map;

/// <summary>DI registration for the map-render feature.</summary>
public static class MapServiceCollectionExtensions
{
    /// <summary>Registers the renderer, base-map source chain, composer, poster, pipeline bundle, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The host configuration (reads Map:RustMaps:ApiKey).</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddMap(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<MapRenderer>();

        // RustMaps is NOT a base-map source (the #map render draws its own layers on the Rust+ tile).
        // The client is kept for the #info static-map surface (Task 7 wires the coordinator/service/poster).
        var rustMapsKey = configuration["Map:RustMaps:ApiKey"];
        if (!string.IsNullOrWhiteSpace(rustMapsKey))
        {
            services.AddRustMapsClientV4(o => o.ApiKey = rustMapsKey);
        }

        services.AddSingleton<IBaseMapSource, RustPlusBaseMapSource>();
        services.AddSingleton<BaseMapCache>();
        services.AddSingleton<MapComposer>();
        services.AddSingleton<IMapChannelPoster, DiscordMapChannelPoster>();
        services.AddSingleton<MapPipeline>();
        services.AddHostedService<MapHostedService>();

        return services;
    }
}
