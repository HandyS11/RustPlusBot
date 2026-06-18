using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRegistrationTests
{
    [Fact]
    public void AddMap_registers_renderer_cache_and_composer_as_singletons()
    {
        using var provider = BuildProvider();

        var renderer = provider.GetRequiredService<MapRenderer>();
        Assert.NotNull(renderer);

        var cache = provider.GetRequiredService<BaseMapCache>();
        Assert.NotNull(cache);

        var composer = provider.GetRequiredService<MapComposer>();
        Assert.NotNull(composer);

        // Singletons: same instance each time.
        Assert.Same(renderer, provider.GetRequiredService<MapRenderer>());
        Assert.Same(cache, provider.GetRequiredService<BaseMapCache>());
        Assert.Same(composer, provider.GetRequiredService<MapComposer>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddSingleton(Substitute.For<IEventState>());
        services.AddSingleton(Substitute.For<IRigState>());
        services.AddScoped(_ => Substitute.For<IMapSettingsStore>());
        services.AddMap();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
