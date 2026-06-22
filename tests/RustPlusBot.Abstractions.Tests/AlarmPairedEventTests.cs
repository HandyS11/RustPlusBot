using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class AlarmPairedEventTests
{
    [Fact]
    public void CarriesIdentity()
    {
        var e = new AlarmPairedEvent(10UL, Guid.Empty, 42UL);
        Assert.Equal(10UL, e.GuildId);
        Assert.Equal(42UL, e.EntityId);
    }
}
