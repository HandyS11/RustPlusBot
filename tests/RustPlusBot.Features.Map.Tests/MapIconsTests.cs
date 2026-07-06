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
        Assert.Equal("oilrig", MonumentIconMap.IconKeyFor("oilrig_1"));
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
}
