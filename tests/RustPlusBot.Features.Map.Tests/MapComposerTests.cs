using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Persistence.Map;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapComposerTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly MapDimensions Dims = new(4000, 4000, 500);

    private static byte[] BaseJpeg()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static IServiceScopeFactory ScopeFactory(IMapSettingsStore settingsStore) =>
        new ServiceCollection()
            .AddScoped(_ => settingsStore)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static MapComposer Build(
        byte[]? baseImage,
        MapDimensions? dims,
        IRustServerQuery query,
        IEventState events,
        IRigState rigs,
        IMapSettingsStore settingsStore)
    {
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(baseImage);
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(dims);
        return new MapComposer(new BaseMapCache(query), events, rigs, query, new MapRenderer(),
            ScopeFactory(settingsStore));
    }

    private static IRustServerQuery NewQuery() => Substitute.For<IRustServerQuery>();

    private static IEventState NewEvents(params ActiveMarker[] markers)
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(Guild, Server, Arg.Any<MarkerKind>())
            .Returns(ci => markers.Where(m => m.Kind == (MarkerKind)ci[2]!).ToList());
        return events;
    }

    private static IRigState NewRigs()
    {
        var rigs = Substitute.For<IRigState>();
        rigs.Get(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<RigKind>())
            .Returns(new RigState(RigStatus.Online, null));
        return rigs;
    }

    private static IMapSettingsStore NewSettings(MapLayerSettings settings)
    {
        var store = Substitute.For<IMapSettingsStore>();
        store.GetAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(settings);
        return store;
    }

    [Fact]
    public async Task ComposeAsync_returns_null_when_no_base_map()
    {
        var composer = Build(baseImage: null, Dims, NewQuery(), NewEvents(), NewRigs(),
            NewSettings(MapLayerSettings.AllOn));

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.Null(png);
    }

    [Fact]
    public async Task Renders_a_png_when_base_map_available()
    {
        var marker = new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow);
        var composer = Build(BaseJpeg(), Dims, NewQuery(), NewEvents(marker), NewRigs(),
            NewSettings(MapLayerSettings.AllOn));

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
    }

    [Fact]
    public async Task Renders_base_only_when_dimensions_unavailable()
    {
        var composer = Build(BaseJpeg(), dims: null, NewQuery(),
            NewEvents(new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow)),
            NewRigs(), NewSettings(MapLayerSettings.AllOn));

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
    }

    [Fact]
    public async Task ComposeAsync_renders_only_enabled_layers()
    {
        // Monuments + Rigs off; Markers + Players on. The composer must NOT touch the monuments seam.
        var query = NewQuery();
        var team = new TeamInfoSnapshot(0,
            [new TeamMemberSnapshot(1, "Ada", 2000f, 2000f, true, true, default, default)]);
        query.GetTeamInfoAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(team);
        var events = NewEvents(new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow));
        var settings = NewSettings(new MapLayerSettings(
            Grid: true, Markers: true, Monuments: false, Vendor: true, Players: true, Rigs: false));

        var composer = Build(BaseJpeg(), Dims, query, events, NewRigs(), settings);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        // Monuments + Rigs both disabled -> no monument round-trip at all.
        await query.DidNotReceive().GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>());
        // Players enabled -> the team seam was consulted.
        await query.Received().GetTeamInfoAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComposeAsync_uses_all_on_when_no_settings_row()
    {
        // A store with no row returns AllOn (its documented default) -> every gather path runs.
        var query = NewQuery();
        query.GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns([new MonumentSnapshot("oilrig_1", 2000f, 2000f)]);
        query.GetTeamInfoAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(0,
                [new TeamMemberSnapshot(1, "Ada", 2000f, 2000f, true, true, default, default)]));
        var events = NewEvents(new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow));

        var composer = Build(BaseJpeg(), Dims, query, events, NewRigs(), NewSettings(MapLayerSettings.AllOn));

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        await query.Received().GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>());
        await query.Received().GetTeamInfoAsync(Guild, Server, Arg.Any<CancellationToken>());
    }
}
