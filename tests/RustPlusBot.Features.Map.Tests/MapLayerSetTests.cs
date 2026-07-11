using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapLayerSetTests
{
    [Fact]
    public void AllOn_enables_every_layer()
    {
        var set = MapLayerSet.AllOn;

        Assert.True(set.Grid);
        Assert.True(set.Markers);
        Assert.True(set.Monuments);
        Assert.True(set.Vendor);
        Assert.True(set.Players);
        Assert.True(set.Rigs);
        Assert.True(set.Tunnels);
    }
}
