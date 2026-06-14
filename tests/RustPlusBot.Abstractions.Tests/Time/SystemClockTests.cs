using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Abstractions.Tests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentInstant_WithinTolerance()
    {
#pragma warning disable CA1859 // Use concrete types for performance — intentional: tests the IClock abstraction
        IClock clock = new SystemClock();
#pragma warning restore CA1859

        var before = DateTimeOffset.UtcNow;
        var value = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(value, before, after);
    }
}
