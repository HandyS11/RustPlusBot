using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class DeviceReachabilityVocabularyTests
{
    [Fact]
    public void DeviceReachability_DefaultIsReachable() =>
        Assert.Equal(DeviceReachability.Reachable, default);

    [Fact]
    public void DeviceReading_CarriesPayloadAndReachability()
    {
        var reading = new DeviceReading(IsActive: true, DeviceReachability.Reachable);
        Assert.True(reading.IsActive);
        Assert.Equal(DeviceReachability.Reachable, reading.Reachability);
    }

    [Fact]
    public void Event_CarriesIdentityAndReachability()
    {
        var serverId = Guid.NewGuid();
        var evt = new DeviceReachabilityChangedEvent(10UL, serverId, 99UL, DeviceReachability.Removed);
        Assert.Equal(10UL, evt.GuildId);
        Assert.Equal(serverId, evt.ServerId);
        Assert.Equal(99UL, evt.EntityId);
        Assert.Equal(DeviceReachability.Removed, evt.Reachability);
    }
}
