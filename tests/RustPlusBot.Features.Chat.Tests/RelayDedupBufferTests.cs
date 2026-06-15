using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Relaying;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class RelayDedupBufferTests
{
    private static (RelayDedupBuffer Buffer, IClock Clock) Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new RelayDedupBuffer(clock), clock);
    }

    [Fact]
    public void Consumes_a_matching_entry_once()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");

        Assert.True(buffer.TryConsume(key, "[Alice] hi"));
        Assert.False(buffer.TryConsume(key, "[Alice] hi")); // already consumed
    }

    [Fact]
    public void Does_not_match_other_text()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");

        Assert.False(buffer.TryConsume(key, "[Bob] hi"));
    }

    [Fact]
    public void Entry_expires_after_ttl()
    {
        var (buffer, clock) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");

        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(1));

        Assert.False(buffer.TryConsume(key, "[Alice] hi"));
    }

    [Fact]
    public void Two_identical_records_consume_independently()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");
        buffer.Record(key, "[Alice] hi");

        Assert.True(buffer.TryConsume(key, "[Alice] hi"));
        Assert.True(buffer.TryConsume(key, "[Alice] hi"));
        Assert.False(buffer.TryConsume(key, "[Alice] hi"));
    }
}
