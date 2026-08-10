using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Tests.Formatting;

public sealed class MapLocationTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly MapDimensions Dims = new(4000u, 4000u, 500, WorldSize: 4000u);

    [Fact]
    public void Inside_the_world_describes_a_grid_cell()
    {
        var location = MapLocation.Describe(Loc, "en", 10f, 3990f, Dims);

        Assert.False(location.IsDirection);
        Assert.Equal("A0", location.Text);
    }

    [Fact]
    public void Outside_the_world_describes_a_direction()
    {
        var location = MapLocation.Describe(Loc, "en", -500f, 4500f, Dims);

        Assert.True(location.IsDirection);
        Assert.Equal("north-west", location.Text);
    }

    [Fact]
    public void Direction_words_are_localized()
    {
        // French direction words carry their article so one message value ("vers {0}") covers all eight.
        Assert.Equal("le nord-ouest", MapLocation.Describe(Loc, "fr", -500f, 4500f, Dims).Text);
        Assert.Equal("l'est", MapLocation.Describe(Loc, "fr", 4500f, 2000f, Dims).Text);
    }

    [Fact]
    public void Null_dimensions_fall_back_to_raw_coordinates()
    {
        var location = MapLocation.Describe(Loc, "en", 1234f, 5678f, dims: null);

        Assert.False(location.IsDirection);
        Assert.Equal("(1234, 5678)", location.Text);
    }

    [Fact]
    public void DescribeDirection_uses_a_direction_even_inside_the_world()
    {
        var location = MapLocation.DescribeDirection(Loc, "en", 2000f, 3900f, Dims);

        Assert.True(location.IsDirection);
        Assert.Equal("north", location.Text);
    }

    [Fact]
    public void DescribeDirection_falls_back_to_raw_coordinates_without_dimensions()
    {
        var location = MapLocation.DescribeDirection(Loc, "en", 1234f, 5678f, dims: null);

        Assert.False(location.IsDirection);
        Assert.Equal("(1234, 5678)", location.Text);
    }
}
