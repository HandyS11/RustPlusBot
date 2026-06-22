using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class SmartDeviceTriggeredEventTests
{
    [Fact]
    public void CarriesIdentityAndState()
    {
        var e = new SmartDeviceTriggeredEvent(10UL, Guid.Empty, 42UL, IsActive: true);
        Assert.Equal(10UL, e.GuildId);
        Assert.Equal(42UL, e.EntityId);
        Assert.True(e.IsActive);
    }
}
