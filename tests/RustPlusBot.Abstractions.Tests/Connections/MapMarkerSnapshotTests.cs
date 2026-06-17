using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class MapMarkerSnapshotTests
{
    [Fact]
    public void Marker_carries_kind_and_coordinates()
    {
        var marker = new MapMarkerSnapshot(42UL, MarkerKind.CargoShip, 1234f, 5678f, "Cargo");

        Assert.Equal(42UL, marker.Id);
        Assert.Equal(MarkerKind.CargoShip, marker.Kind);
        Assert.Equal(1234f, marker.X);
        Assert.Equal(5678f, marker.Y);
        Assert.Equal("Cargo", marker.Name);
    }

    [Fact]
    public void Dimensions_carry_size_and_margin()
    {
        var dims = new MapDimensions(4000u, 4000u, 500);

        Assert.Equal(4000u, dims.Width);
        Assert.Equal(500, dims.OceanMargin);
    }
}
