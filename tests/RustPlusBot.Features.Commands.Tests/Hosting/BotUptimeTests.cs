using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Hosting;

namespace RustPlusBot.Features.Commands.Tests.Hosting;

public sealed class BotUptimeTests
{
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch; }

    [Fact]
    public void Elapsed_IsDifferenceSinceConstruction()
    {
        var clock = new TestClock();
        var uptime = new BotUptime(clock); // captures start at construction
        clock.UtcNow = clock.UtcNow.AddMinutes(90);
        Assert.Equal(TimeSpan.FromMinutes(90), uptime.Elapsed);
    }
}
