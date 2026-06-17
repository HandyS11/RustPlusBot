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

    private static MapComposer Build(byte[]? baseImage, params ActiveMarker[] markers)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(baseImage);
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(Guild, Server, Arg.Any<MarkerKind>())
            .Returns(ci => markers.Where(m => m.Kind == (MarkerKind)ci[2]!).ToList());
        return new MapComposer(new BaseMapCache(query), events, new MapRenderer());
    }

    [Fact]
    public async Task Returns_null_when_no_base_map_available()
    {
        var composer = Build(baseImage: null);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.Null(png);
    }

    [Fact]
    public async Task Renders_a_png_when_base_map_available()
    {
        var marker = new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow);
        var composer = Build(BaseJpeg(), marker);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
    }
}
