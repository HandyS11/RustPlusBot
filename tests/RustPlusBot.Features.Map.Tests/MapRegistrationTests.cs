using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
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
        using var provider = BuildProvider(EmptyConfiguration());

        var renderer = provider.GetRequiredService<MapRenderer>();
        Assert.NotNull(renderer);

        var source = provider.GetRequiredService<IBaseMapSource>();
        Assert.NotNull(source);

        var cache = provider.GetRequiredService<BaseMapCache>();
        Assert.NotNull(cache);

        var composer = provider.GetRequiredService<MapComposer>();
        Assert.NotNull(composer);

        // Singletons: same instance each time.
        Assert.Same(renderer, provider.GetRequiredService<MapRenderer>());
        Assert.Same(cache, provider.GetRequiredService<BaseMapCache>());
        Assert.Same(composer, provider.GetRequiredService<MapComposer>());
    }

    [Fact]
    public void AddMap_with_RustMaps_key_registers_RustMaps_source_first()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:RustMaps:ApiKey", "test-key")])
            .Build();
        using var provider = BuildProvider(configuration);

        var sources = provider.GetServices<IBaseMapSource>().ToList();

        Assert.Equal(2, sources.Count);
        Assert.IsType<RustMapsBaseMapSource>(sources[0]);
        Assert.IsType<RustPlusBaseMapSource>(sources[1]);
    }

    [Fact]
    public void AddMap_without_RustMaps_key_registers_only_the_RustPlus_source()
    {
        using var provider = BuildProvider(EmptyConfiguration());

        var sources = provider.GetServices<IBaseMapSource>().ToList();

        var source = Assert.Single(sources);
        Assert.IsType<RustPlusBaseMapSource>(source);
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddSingleton(Substitute.For<IEventState>());
        services.AddSingleton(Substitute.For<IRigState>());
        services.AddScoped(_ => Substitute.For<IMapSettingsStore>());
        services.AddMap(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }
}
