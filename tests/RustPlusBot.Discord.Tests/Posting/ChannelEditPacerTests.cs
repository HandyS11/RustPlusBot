using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Discord.Tests.Posting;

public sealed class ChannelEditPacerTests
{
    private const ulong ChannelA = 500;
    private const ulong ChannelB = 501;
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(1);

    [Fact]
    public void First_edit_to_a_channel_waits_zero()
    {
        var pacer = new ChannelEditPacer(new TestClock(), Gap);

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(ChannelA));
    }

    [Fact]
    public void Successive_edits_in_the_same_instant_are_staggered_by_the_gap()
    {
        var pacer = new ChannelEditPacer(new TestClock(), Gap);

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(ChannelA));
        Assert.Equal(Gap, pacer.Reserve(ChannelA));
        Assert.Equal(Gap * 2, pacer.Reserve(ChannelA));
    }

    [Fact]
    public void An_edit_after_the_gap_has_elapsed_waits_zero()
    {
        var clock = new TestClock();
        var pacer = new ChannelEditPacer(clock, Gap);

        pacer.Reserve(ChannelA);
        clock.UtcNow = clock.UtcNow.Add(Gap);

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(ChannelA));
    }

    [Fact]
    public void An_edit_partway_through_the_gap_waits_only_the_remainder()
    {
        var clock = new TestClock();
        var pacer = new ChannelEditPacer(clock, Gap);

        pacer.Reserve(ChannelA);
        clock.UtcNow = clock.UtcNow.Add(TimeSpan.FromMilliseconds(400));

        Assert.Equal(TimeSpan.FromMilliseconds(600), pacer.Reserve(ChannelA));
    }

    [Fact]
    public void Channels_are_paced_independently()
    {
        var pacer = new ChannelEditPacer(new TestClock(), Gap);

        pacer.Reserve(ChannelA);

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(ChannelB));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }
}
