using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRendererTests
{
    private static readonly MapDimensions Dims = new(Width: 4000, Height: 4000, OceanMargin: 500);

    /// <summary>A 64x64 solid-green JPEG, generated once in-test so the renderer has a real base image to decode.</summary>
    private static byte[] BaseJpeg()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Render_produces_a_png_of_the_output_size()
    {
        var renderer = new MapRenderer();

        var bytes = renderer.Render(BaseJpeg(), Dims, markers: [], monuments: [], players: [], rigs: [],
            MapLayerSet.AllOn);

        using var result = Image.Load<Rgba32>(bytes);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
        Assert.Equal(MapRenderer.OutputSize, result.Height);
    }

    [Fact]
    public void Render_with_a_marker_differs_from_render_without()
    {
        var renderer = new MapRenderer();
        var jpeg = BaseJpeg();

        var without = renderer.Render(jpeg, Dims, markers: [], monuments: [], players: [], rigs: [],
            new MapLayerSet(false, true, false, false, false, false));
        var with = renderer.Render(jpeg, Dims,
            markers: [new MarkerPlacement(MarkerKind.CargoShip, 512f, 512f)],
            monuments: [], players: [], rigs: [],
            new MapLayerSet(false, true, false, false, false, false));

        Assert.NotEqual(without, with); // The drawn marker changes the bytes.
    }

    [Fact]
    public void Render_with_all_layers_produces_valid_png()
    {
        var renderer = new MapRenderer();
        var markers = new[]
        {
            new MarkerPlacement(MarkerKind.CargoShip, 100, 100)
        };
        var monuments = new[]
        {
            new MonumentPlacement("launchsite", 200, 200)
        };
        var players = new[]
        {
            new PlayerPlacement("Alice", 300, 300, IsAlive: true, IsOnline: true)
        };
        var rigs = new[]
        {
            new RigPlacement(RigKind.Large, 400, 400, Active: true)
        };

        var png = renderer.Render(BaseJpeg(), Dims, markers, monuments, players, rigs, MapLayerSet.AllOn);

        using var img = Image.Load(png); // throws if not a valid image
        Assert.Equal(MapRenderer.OutputSize, img.Width);
    }
}
