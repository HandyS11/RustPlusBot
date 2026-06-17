using RustPlusBot.Features.Map.Rendering;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapLayerSetTests
{
    [Fact]
    public void Default2b_enables_grid_and_markers_only()
    {
        var set = MapLayerSet.Default2b;

        Assert.True(set.Grid);
        Assert.True(set.Markers);
        Assert.False(set.Monuments);
        Assert.False(set.Vendor);
        Assert.False(set.Rigs);
    }
}
