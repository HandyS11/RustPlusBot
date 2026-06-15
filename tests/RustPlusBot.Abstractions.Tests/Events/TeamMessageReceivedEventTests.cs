using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests.Events;

public sealed class TeamMessageReceivedEventTests
{
    [Fact]
    public void Carries_all_fields()
    {
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 7656119UL, "Alice", "hello", FromActivePlayer: true);

        Assert.Equal(10UL, evt.GuildId);
        Assert.Equal(Guid.Empty, evt.ServerId);
        Assert.Equal(7656119UL, evt.SenderSteamId);
        Assert.Equal("Alice", evt.SenderName);
        Assert.Equal("hello", evt.Message);
        Assert.True(evt.FromActivePlayer);
    }
}
