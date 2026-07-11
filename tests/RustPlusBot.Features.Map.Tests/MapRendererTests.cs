using Microsoft.Extensions.Logging.Abstractions;
using RustMapsApi.V4.Assets;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Map.Assets;
using RustPlusBot.Features.Map.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRendererTests
{
    private static readonly MapProjection Projection =
        new(WorldSize: 4000, ImageWidth: 4000, ImageHeight: 4000, OceanMarginPx: 500,
            OutputSize: MapRenderer.OutputSize);

    private static MapRenderer CreateRenderer() =>
        new(new MonumentIconSource(new MonumentAssetSource(), NullLogger<MonumentIconSource>.Instance));

    /// <summary>A 64x64 solid-green JPEG, generated once in-test so the renderer has a real base image to decode.</summary>
    private static byte[] BaseJpeg()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static byte[] SolidJpeg(int size)
    {
        using var img = new Image<Rgba32>(size, size, new Rgba32(40, 90, 120));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static Rectangle ChangedPixelBounds(byte[] a, byte[] b)
    {
        using var ia = Image.Load<Rgba32>(a);
        using var ib = Image.Load<Rgba32>(b);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < ia.Height; y++)
        {
            for (var x = 0; x < ia.Width; x++)
            {
                if (ia[x, y] != ib[x, y])
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    [Fact]
    public void Render_produces_a_png_of_the_output_size()
    {
        var renderer = CreateRenderer();

        var bytes = renderer.Render(BaseJpeg(), Projection, markers: [], monuments: [], players: [], rigs: [],
            MapLayerSet.AllOn);

        using var result = Image.Load<Rgba32>(bytes);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
        Assert.Equal(MapRenderer.OutputSize, result.Height);
    }

    [Fact]
    public void Render_with_a_marker_differs_from_render_without()
    {
        var renderer = CreateRenderer();
        var jpeg = BaseJpeg();

        var without = renderer.Render(jpeg, Projection, markers: [], monuments: [], players: [], rigs: [],
            new MapLayerSet(false, true, false, false, false, false));
        var with = renderer.Render(jpeg, Projection,
            markers: [new MarkerPlacement(MarkerKind.CargoShip, 512f, 512f, null, [])],
            monuments: [], players: [], rigs: [],
            new MapLayerSet(false, true, false, false, false, false));

        Assert.NotEqual(without, with); // The drawn marker changes the bytes.
    }

    [Fact]
    public void Render_with_all_layers_produces_valid_png()
    {
        var renderer = CreateRenderer();
        var markers = new[]
        {
            new MarkerPlacement(MarkerKind.CargoShip, 100, 100, null, [])
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

        var png = renderer.Render(BaseJpeg(), Projection, markers, monuments, players, rigs, MapLayerSet.AllOn);

        using var img = Image.Load(png); // throws if not a valid image
        Assert.Equal(MapRenderer.OutputSize, img.Width);
    }

    [Fact]
    public void Monument_icon_is_drawn_scaled_not_native()
    {
        var renderer = CreateRenderer();
        var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
        var baseJpeg = SolidJpeg(2000);
        var (px, py) = projection.ToPixel(2000f, 2000f);

        var without = renderer.Render(baseJpeg, projection, [], [], [], [],
            new MapLayerSet(false, false, false, false, false, false));
        var with = renderer.Render(baseJpeg, projection, [],
            [new MonumentPlacement("oil_rig_small", px, py)], [], [],
            new MapLayerSet(false, false, true, false, false, false));

        var bounds = ChangedPixelBounds(without, with);
        Assert.True(bounds.Width <= MapRenderStyle.MonumentIconSize + 2,
            $"changed area {bounds.Width}px wide — icon not scaled");
        Assert.True(bounds.Height <= MapRenderStyle.MonumentIconSize + 2);
    }

    [Fact]
    public void Trail_draws_pixels_between_history_points()
    {
        var renderer = CreateRenderer();
        var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
        var baseJpeg = SolidJpeg(2000);
        var (ax, ay) = projection.ToPixel(1000f, 2000f);
        var (bx, by) = projection.ToPixel(2000f, 2000f);
        var layers = new MapLayerSet(false, true, false, false, false, false);

        var without = renderer.Render(baseJpeg, projection,
            [new MarkerPlacement(MarkerKind.CargoShip, bx, by, null, [])], [], [], [], layers);
        var with = renderer.Render(baseJpeg, projection,
        [
            new MarkerPlacement(MarkerKind.CargoShip, bx, by, null,
                [new PointF(ax, ay), new PointF(bx, by)])
        ], [], [], [], layers);

        var bounds = ChangedPixelBounds(without, with);
        // The trail spans from A to B — far wider than the icon alone.
        Assert.True(bounds.Width > MapRenderStyle.CargoIconSize * 2, $"no trail drawn (width {bounds.Width}px)");
    }

    [Fact]
    public void Grid_style_shifts_the_rendered_rows()
    {
        // Rust+/RustMaps rows sit 100 world-units south of the in-game rows, so the two styles must
        // produce different grid pixels on the same base image.
        var renderer = CreateRenderer();
        var projection = new MapProjection(1500, 2000, 2000, 100, MapRenderer.OutputSize);
        var baseJpeg = SolidJpeg(2000);
        var layers = new MapLayerSet(Grid: true, Markers: false, Monuments: false, Vendor: false,
            Players: false, Rigs: false);

        var inGame = renderer.Render(baseJpeg, projection, [], [], [], [], layers);
        var rustPlus = renderer.Render(baseJpeg, projection, [], [], [], [], layers, MapGridStyle.RustPlus);

        Assert.False(inGame.AsSpan().SequenceEqual(rustPlus));
    }

    [Fact]
    public void Rotation_changes_the_rendered_icon()
    {
        var renderer = CreateRenderer();
        var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
        var baseJpeg = SolidJpeg(2000);
        var (px, py) = projection.ToPixel(2000f, 2000f);
        var layers = new MapLayerSet(false, true, false, false, false, false);

        var unrotated = renderer.Render(baseJpeg, projection,
            [new MarkerPlacement(MarkerKind.CargoShip, px, py, null, [])], [], [], [], layers);
        var rotated = renderer.Render(baseJpeg, projection,
            [new MarkerPlacement(MarkerKind.CargoShip, px, py, 45f, [])], [], [], [], layers);

        Assert.False(unrotated.AsSpan().SequenceEqual(rotated));
    }
}
