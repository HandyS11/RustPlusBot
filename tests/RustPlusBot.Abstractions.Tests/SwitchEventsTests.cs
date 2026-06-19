using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class SwitchEventsTests
{
    [Fact]
    public void SwitchPairedEvent_carries_guild_server_entity()
    {
        var serverId = Guid.NewGuid();
        var evt = new SwitchPairedEvent(10UL, serverId, 42UL);

        Assert.Equal(10UL, evt.GuildId);
        Assert.Equal(serverId, evt.ServerId);
        Assert.Equal(42UL, evt.EntityId);
    }

    [Fact]
    public void SwitchStateChangedEvent_carries_state()
    {
        var serverId = Guid.NewGuid();
        var evt = new SwitchStateChangedEvent(10UL, serverId, 42UL, IsActive: true);

        Assert.Equal(10UL, evt.GuildId);
        Assert.Equal(serverId, evt.ServerId);
        Assert.Equal(42UL, evt.EntityId);
        Assert.True(evt.IsActive);
    }
}
