using NSubstitute;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Rendering;
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

    private static MapComposer Build(byte[]? baseImage, MapDimensions? dims, params ActiveMarker[] markers)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(baseImage);
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(dims);
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(Guild, Server, Arg.Any<MarkerKind>())
            .Returns(ci => markers.Where(m => m.Kind == (MarkerKind)ci[2]!).ToList());
        return new MapComposer(new BaseMapCache(query), events, query, new MapRenderer());
    }

    [Fact]
    public async Task Returns_null_when_no_base_map_available()
    {
        var composer = Build(baseImage: null, dims: Dims);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.Null(png);
    }

    [Fact]
    public async Task Renders_a_png_when_base_map_available()
    {
        var marker = new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow);
        var composer = Build(BaseJpeg(), Dims, marker);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
    }

    [Fact]
    public async Task Renders_the_grid_even_with_no_markers()
    {
        // Dimensions come from the query seam, not from a marker, so a connected server with no active
        // markers still renders the gridded map (not a bare base tile).
        var composer = Build(BaseJpeg(), Dims);

        var withoutGrid = new MapRenderer().Render(BaseJpeg(), Dims, markers: [], monuments: [], players: [], rigs: [],
            new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false, Rigs: false));
        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
        // The grid was drawn: the gridded output differs from a base-only (grid-off) render.
        Assert.NotEqual(withoutGrid, png);
    }

    [Fact]
    public async Task Renders_base_only_when_dimensions_unavailable()
    {
        var composer = Build(BaseJpeg(), dims: null,
            new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow));

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
    }
}
