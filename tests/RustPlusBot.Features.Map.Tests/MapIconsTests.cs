using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Assets;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapIconsTests
{
    [Theory]
    [InlineData(MarkerKind.CargoShip)]
    [InlineData(MarkerKind.PatrolHelicopter)]
    [InlineData(MarkerKind.Chinook)]
    public void Marker_resolves_for_known_kinds(MarkerKind kind) =>
        Assert.NotNull(MapIcons.Marker(kind));

    [Fact]
    public void Sized_marker_icon_fits_the_requested_box()
    {
        var icon = MapIcons.Marker(MarkerKind.CargoShip, 40);

        Assert.NotNull(icon);
        Assert.True(icon!.Width <= 40 && icon.Height <= 40);
        Assert.True(icon.Width == 40 || icon.Height == 40); // aspect-preserving fit, longest edge = size
    }

    [Fact]
    public void Sized_icons_are_cached_per_size()
    {
        Assert.Same(MapIcons.Marker(MarkerKind.CargoShip, 40), MapIcons.Marker(MarkerKind.CargoShip, 40));
        Assert.NotSame(MapIcons.Marker(MarkerKind.CargoShip, 40), MapIcons.Marker(MarkerKind.CargoShip, 44));
    }
}
