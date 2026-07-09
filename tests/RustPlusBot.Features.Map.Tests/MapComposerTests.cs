using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Persistence.Map;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapComposerTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly MapDimensions Dims = new(2000, 2000, 100, 4000);

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
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(dims);
        var source = new FakeSource(
            baseImage is null
                ? null
                : new BaseMapImage(baseImage, (int)Dims.Width, (int)Dims.Height, Dims.OceanMargin));
        return new MapComposer(new BaseMapCache([source]), events, rigs, query, new MapRenderer(),
            ScopeFactory(settingsStore));
    }

    private static IRustServerQuery NewQuery() => Substitute.For<IRustServerQuery>();

    private static IEventState NewEvents(params ActiveMarker[] markers)
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(Guild, Server, Arg.Any<MarkerKind>())
            .Returns(ci => [.. markers.Where(m => m.Kind == (MarkerKind)ci[2]!)]);
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
        var marker = new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow,
            [new TrailPoint(2000f, 2000f)], null);
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
            NewEvents(new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow,
                [new TrailPoint(2000f, 2000f)], null)),
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
        var events = NewEvents(new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow,
            [new TrailPoint(2000f, 2000f)], null));
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
        var events = NewEvents(new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow,
            [new TrailPoint(2000f, 2000f)], null));

        var composer = Build(BaseJpeg(), Dims, query, events, NewRigs(), NewSettings(MapLayerSettings.AllOn));

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        await query.Received().GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>());
        await query.Received().GetTeamInfoAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_the_grid_even_with_no_markers()
    {
        // Regression guard: on a low-activity / just-connected server there are zero active markers.
        // The composer must still render the grid (and produce a different image than with grid off),
        // proving it does NOT early-return on an empty marker list.
        var jpeg = BaseJpeg();

        var composerOn = Build(jpeg, Dims, NewQuery(), NewEvents(), NewRigs(),
            NewSettings(new MapLayerSettings(
                Grid: true, Markers: true, Monuments: false, Vendor: false, Players: false, Rigs: false)));
        var composerOff = Build(jpeg, Dims, NewQuery(), NewEvents(), NewRigs(),
            NewSettings(new MapLayerSettings(
                Grid: false, Markers: true, Monuments: false, Vendor: false, Players: false, Rigs: false)));

        var pngOn = await composerOn.ComposeAsync(Guild, Server, CancellationToken.None);
        var pngOff = await composerOff.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(pngOn);
        Assert.NotNull(pngOff);
        // The grid layer must have painted at least one pixel differently.
        Assert.False(pngOn!.SequenceEqual(pngOff!), "Grid-on and grid-off renders must differ.");
    }

    [Fact]
    public async Task ComposeAsync_forwards_vendor_marker_history_into_rendered_trail()
    {
        // Regression guard for the Task-8 ProjectTrail refactor: GatherMarkers's Vendor branch must
        // forward each marker's History ring through to the rendered trail, not just the current point.
        var jpeg = BaseJpeg();
        var vendorWithTrail = new ActiveMarker(1, MarkerKind.TravellingVendor, 2000f, 2000f, Dims,
            DateTimeOffset.UtcNow, [new TrailPoint(1200f, 1200f), new TrailPoint(2000f, 2000f)], null);
        var vendorNoTrail = new ActiveMarker(1, MarkerKind.TravellingVendor, 2000f, 2000f, Dims,
            DateTimeOffset.UtcNow, [new TrailPoint(2000f, 2000f)], null);
        var layers = new MapLayerSettings(
            Grid: false, Markers: false, Monuments: false, Vendor: true, Players: false, Rigs: false);

        var composerWithTrail =
            Build(jpeg, Dims, NewQuery(), NewEvents(vendorWithTrail), NewRigs(), NewSettings(layers));
        var composerNoTrail =
            Build(jpeg, Dims, NewQuery(), NewEvents(vendorNoTrail), NewRigs(), NewSettings(layers));

        var pngWithTrail = await composerWithTrail.ComposeAsync(Guild, Server, CancellationToken.None);
        var pngNoTrail = await composerNoTrail.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(pngWithTrail);
        Assert.NotNull(pngNoTrail);
        // MapRenderer.DrawTrails only paints when Trail.Count >= 2, so a 2-point History must render
        // differently from a 1-point History — proving the history ring made it through to the trail.
        Assert.False(pngWithTrail!.SequenceEqual(pngNoTrail!),
            "Vendor trail with 2-point history must render differently than a 1-point history.");
    }

    private sealed class FakeSource(BaseMapImage? result) : IBaseMapSource
    {
        public Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
