using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapProjectionTests
{
    [Fact]
    public void Rust_plus_style_margin_projects_origin_inside_margin()
    {
        // 4000-unit world on a 2000px image with 100px margin, output 1000px.
        // Playable spans [100, 1900]px on the image -> world(0,0) at image (100, 1900) -> output (50, 950).
        var p = new MapProjection(WorldSize: 4000, ImageWidth: 2000, ImageHeight: 2000, OceanMarginPx: 100, OutputSize: 1000);

        var (x, y) = p.ToPixel(0f, 0f);

        Assert.Equal(50f, x, precision: 3);
        Assert.Equal(950f, y, precision: 3);
    }

    [Fact]
    public void Center_of_world_is_center_of_output()
    {
        var p = new MapProjection(4000, 2000, 2000, 100, 1000);

        var (x, y) = p.ToPixel(2000f, 2000f);

        Assert.Equal(500f, x, precision: 3);
        Assert.Equal(500f, y, precision: 3);
    }

    [Fact]
    public void Rustmaps_style_zero_margin_maps_world_to_full_image()
    {
        // RustMaps raw render: no ocean margin. World (0,0) -> output bottom-left corner.
        var p = new MapProjection(3500, 1750, 1750, 0, 1024);

        var (x0, y0) = p.ToPixel(0f, 0f);
        var (x1, y1) = p.ToPixel(3500f, 3500f);

        Assert.Equal(0f, x0, precision: 3);
        Assert.Equal(1024f, y0, precision: 3);
        Assert.Equal(1024f, x1, precision: 3);
        Assert.Equal(0f, y1, precision: 3);
    }

    [Fact]
    public void North_is_visually_above_south()
    {
        var p = new MapProjection(4000, 2000, 2000, 100, 1000);

        var (_, southY) = p.ToPixel(2000f, 0f);
        var (_, northY) = p.ToPixel(2000f, 4000f);

        Assert.True(northY < southY);
    }
}
