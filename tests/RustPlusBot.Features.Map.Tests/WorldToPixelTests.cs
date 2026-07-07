using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

public sealed class WorldToPixelTests
{
    private static readonly MapDimensions Dims = new(Width: 4000, Height: 4000, OceanMargin: 500, WorldSize: 4000);

    [Fact]
    public void Origin_world_maps_to_bottom_left_inside_margin()
    {
        // World (0,0) is the SW corner of the playable area. Full tile spans [-500, 4500] = 5000 units.
        // Pixel-per-unit at outputSize 1000 = 1000/5000 = 0.2. World x=0 -> (0 - (-500)) * 0.2 = 100.
        // World y=0 is the bottom -> image y = outputSize - 100 = 900.
        var (px, py) = WorldToPixel.ToPixel(0f, 0f, Dims, outputSize: 1000);

        Assert.Equal(100f, px, precision: 3);
        Assert.Equal(900f, py, precision: 3);
    }

    [Fact]
    public void Center_world_maps_to_center_pixel()
    {
        var (px, py) = WorldToPixel.ToPixel(2000f, 2000f, Dims, outputSize: 1000);

        Assert.Equal(500f, px, precision: 3);
        Assert.Equal(500f, py, precision: 3);
    }

    [Fact]
    public void North_edge_maps_higher_than_south_edge()
    {
        var (_, southY) = WorldToPixel.ToPixel(2000f, 0f, Dims, outputSize: 1000);
        var (_, northY) = WorldToPixel.ToPixel(2000f, 4000f, Dims, outputSize: 1000);

        Assert.True(northY < southY); // North is visually higher = smaller image-Y.
    }
}
