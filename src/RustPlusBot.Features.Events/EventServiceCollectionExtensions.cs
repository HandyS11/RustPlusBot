using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Hosting;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events;

/// <summary>DI registration for the live-events feature.</summary>
public static class EventServiceCollectionExtensions
{
    /// <summary>Registers the classifier, state store, renderer, relay, poster, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<EventStateStore>();
        services.AddSingleton<IEventState>(sp => sp.GetRequiredService<EventStateStore>());
        services.AddSingleton<RigStateStore>();
        services.AddSingleton<IRigState>(sp => sp.GetRequiredService<RigStateStore>());
        services.AddSingleton(EventLocalizationCatalog.Default);
        services.AddSingleton<IEventLocalizer, EventLocalizer>();
        services.AddSingleton<MarkerEventClassifier>();
        services.AddSingleton<EventEmbedRenderer>();
        services.AddSingleton<IEventChannelPoster, DiscordEventChannelPoster>();
        services.AddSingleton<EventRelay>();
        services.AddHostedService<EventsHostedService>();

        return services;
    }
}
