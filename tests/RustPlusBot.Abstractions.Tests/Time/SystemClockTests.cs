using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Abstractions.Tests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentInstant_WithinTolerance()
    {
        IClock clock = new SystemClock();

        var before = DateTimeOffset.UtcNow;
        var value = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(value, before, after);
    }
}
