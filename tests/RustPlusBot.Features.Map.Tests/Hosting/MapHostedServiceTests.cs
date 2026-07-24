using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustMapsApi.V4.Assets;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Assets;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Map.Tests.Hosting;

public sealed class MapHostedServiceTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    /// <summary>A clock that jumps an hour per read — past <see cref="MapOptions.MapRefreshInterval"/> below,
    /// so the throttle passes every refresh and each consumed event reaches the counter (never gates).</summary>
    private static IClock AdvancingClock()
    {
        var ticks = 0;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => DateTimeOffset.UnixEpoch + TimeSpan.FromHours(ticks++));
        return clock;
    }

    private static IServiceScopeFactory ScopeFactory(IConnectionStore store) =>
        new ServiceCollection()
            .AddScoped(_ => store)
            .AddScoped(_ => Substitute.For<IMapSettingsStore>())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static IConnectionStore ConnectedStore()
    {
        var store = Substitute.For<IConnectionStore>();
        store.GetStateAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = Guild, RustServerId = Server, Status = ConnectionStatus.Connected
            });
        return store;
    }

    private static MapComposer Composer(IServiceScopeFactory scopeFactory) =>
        new(new BaseMapCache([]), Substitute.For<IEventState>(), Substitute.For<IRigState>(),
            Substitute.For<IRustServerQuery>(),
            new MapRenderer(new MonumentIconSource(new MonumentAssetSource(), NullLogger<MonumentIconSource>.Instance)),
            scopeFactory);

    [Fact]
    public async Task Status_loop_survives_a_faulting_refresh_and_keeps_consuming_events()
    {
        // A transient failure anywhere in the refresh path (a Rust+ query, Discord, …) used to escape the
        // await-foreach and end the subscription for the rest of the process: the #map then went silent
        // until restart. One bad refresh must degrade that refresh only.
        var locator = Substitute.For<IMapChannelLocator>();
        var calls = 0;
        locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns<ulong?>(_ => ++calls == 1
                ? throw new InvalidOperationException("GetMap returned no data.")
                : null);

        var scopeFactory = ScopeFactory(ConnectedStore());
        var pipeline = new MapPipeline(Composer(scopeFactory), new BaseMapCache([]), locator,
            Substitute.For<IMapChannelPoster>());
        var bus = new InMemoryEventBus();
        var options = Options.Create(new MapOptions
        {
            MapRefreshInterval = TimeSpan.FromMinutes(30)
        });
        var service = new MapHostedService(bus, pipeline, AdvancingClock(), options, scopeFactory,
            NullLogger<MapHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // The bus drops events published before the subscriber is live, so publish until observed.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref calls) < 2)
            {
                await bus.PublishAsync(new ConnectionStatusChangedEvent(Guild, Server, true, false));
                await Task.Delay(20);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(Volatile.Read(ref calls) >= 2,
            $"the status loop stopped consuming after the first refresh threw (calls: {calls})");
    }
}
