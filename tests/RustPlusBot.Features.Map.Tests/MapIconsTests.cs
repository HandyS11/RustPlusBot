using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
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
    public void Monument_resolves_known_token() =>
        Assert.NotNull(MapIcons.Monument("launchsite"));

    [Fact]
    public void Monument_returns_null_for_unknown_token() =>
        Assert.Null(MapIcons.Monument("definitely_not_a_monument"));

    [Fact]
    public void MonumentIconMap_maps_rig_tokens_to_icons()
    {
        Assert.Equal("oilrig", MonumentIconMap.IconKeyFor("oil_rig_small"));
        Assert.Equal("largeoilrig", MonumentIconMap.IconKeyFor("large_oil_rig"));
    }

    [Theory]
    [InlineData(RigKind.Small)]
    [InlineData(RigKind.Large)]
    public void Rig_resolves_for_known_kinds(RigKind kind) =>
        Assert.NotNull(MapIcons.Rig(kind, active: false));

    [Fact]
    public void Player_resolves_to_vendored_icon() =>
        Assert.NotNull(MapIcons.Player());

    [Fact]
    public void Sized_marker_icon_fits_the_requested_box()
    {
        var icon = MapIcons.Marker(MarkerKind.CargoShip, 40);

        Assert.NotNull(icon);
        Assert.True(icon!.Width <= 40 && icon.Height <= 40);
        Assert.True(icon.Width == 40 || icon.Height == 40); // aspect-preserving fit, longest edge = size
    }

    [Fact]
    public void Sized_monument_icon_is_scaled_down_from_native()
    {
        var native = MapIcons.Monument("oil_rig_small"); // 875x875 native
        var sized = MapIcons.Monument("oil_rig_small", 30);

        Assert.NotNull(native);
        Assert.NotNull(sized);
        Assert.True(sized!.Width <= 30 && sized.Height <= 30);
    }

    [Fact]
    public void Sized_icons_are_cached_per_size()
    {
        Assert.Same(MapIcons.Player(20), MapIcons.Player(20));
        Assert.NotSame(MapIcons.Player(20), MapIcons.Player(24));
    }
}
