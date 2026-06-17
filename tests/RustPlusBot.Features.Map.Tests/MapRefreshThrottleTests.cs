using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Map.Hosting;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRefreshThrottleTests
{
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    [Fact]
    public void First_call_allows_then_blocks_within_interval()
    {
        var clock = new TestClock();
        var throttle = new MapRefreshThrottle(clock);

        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        Assert.False(throttle.ShouldRefresh(1UL, Server, Interval));
    }

    [Fact]
    public void Allows_again_after_interval_elapses()
    {
        var clock = new TestClock();
        var throttle = new MapRefreshThrottle(clock);

        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
        clock.UtcNow = clock.UtcNow.AddSeconds(46);
        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
    }

    [Fact]
    public void Separate_servers_throttle_independently()
    {
        var clock = new TestClock();
        var throttle = new MapRefreshThrottle(clock);
        var other = Guid.NewGuid();

        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
        Assert.True(throttle.ShouldRefresh(1UL, other, Interval));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }
}
