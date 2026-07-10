using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests.Events;

public sealed class RigStateChangedEventTests
{
    [Fact]
    public void Carries_rig_kind_event_kind_position_and_dimensions()
    {
        var server = Guid.NewGuid();
        var dims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);

        var evt = new RigStateChangedEvent(7UL, server, RigKind.Large, RigEventKind.CrateLootable, 1f, 2f, dims);

        Assert.Equal(7UL, evt.GuildId);
        Assert.Equal(server, evt.ServerId);
        Assert.Equal(RigKind.Large, evt.Rig);
        Assert.Equal(RigEventKind.CrateLootable, evt.Kind);
        Assert.Equal(1f, evt.X);
        Assert.Equal(2f, evt.Y);
        Assert.Equal(dims, evt.Dimensions);
    }

    [Fact]
    public void Monument_snapshot_carries_token_and_position()
    {
        var m = new MonumentSnapshot("oil_rig_small", 10f, 20f);
        Assert.Equal("oil_rig_small", m.Token);
        Assert.Equal(10f, m.X);
        Assert.Equal(20f, m.Y);
    }
}
