using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Map;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Features.Map.RustMaps;
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
    public void AddMap_registers_only_the_RustPlus_base_source_without_key()
    {
        using var provider = BuildProvider(EmptyConfiguration());
        var source = Assert.Single(provider.GetServices<IBaseMapSource>());
        Assert.IsType<RustPlusBaseMapSource>(source);
    }

    [Fact]
    public void AddMap_with_RustMaps_key_still_uses_only_the_RustPlus_base_source()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:RustMaps:ApiKey", "test-key")])
            .Build();
        using var provider = BuildProvider(configuration);
        var source = Assert.Single(provider.GetServices<IBaseMapSource>());
        Assert.IsType<RustPlusBaseMapSource>(source);
    }

    [Fact]
    public void AddMap_with_RustMaps_key_registers_the_generation_components()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddSingleton(Substitute.For<IEventState>());
        services.AddSingleton(Substitute.For<IRigState>());
        services.AddScoped(_ => Substitute.For<IMapSettingsStore>());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:RustMaps:ApiKey", "test-key")])
            .Build();
        services.AddMap(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var coordinator = provider.GetService<IRustMapsMapCoordinator>();
        var readModel = provider.GetService<IInfoMapReadModel>();
        Assert.NotNull(coordinator);
        Assert.NotNull(readModel);
        // IRustMapsMapCoordinator and IInfoMapReadModel must resolve to the SAME singleton so the driver's
        // writes are visible to the Workspace renderer's reads.
        Assert.Same(coordinator, readModel);
        Assert.NotNull(provider.GetService<RustMapsGenerationDriver>());
        // Discord-dependent — assert registration without constructing DiscordSocketClient.
        Assert.Contains(services, d => d.ImplementationType == typeof(InfoMapHostedService));
    }

    [Fact]
    public void AddMap_without_RustMaps_key_registers_no_generation_components()
    {
        using var provider = BuildProvider(EmptyConfiguration());
        Assert.Null(provider.GetService<IRustMapsMapCoordinator>());
        Assert.Null(provider.GetService<IInfoMapReadModel>());
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
