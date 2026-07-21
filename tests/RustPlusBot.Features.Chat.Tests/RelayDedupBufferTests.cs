using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Time;

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
        buffer.Record(ChatChannelKind.Team, key, "[Alice] hi");

        Assert.True(buffer.TryConsume(ChatChannelKind.Team, key, "[Alice] hi"));
        Assert.False(buffer.TryConsume(ChatChannelKind.Team, key, "[Alice] hi")); // already consumed
    }

    [Fact]
    public void Does_not_match_other_text()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(ChatChannelKind.Team, key, "[Alice] hi");

        Assert.False(buffer.TryConsume(ChatChannelKind.Team, key, "[Bob] hi"));
    }

    [Fact]
    public void Entry_expires_after_ttl()
    {
        var (buffer, clock) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(ChatChannelKind.Team, key, "[Alice] hi");

        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(1));

        Assert.False(buffer.TryConsume(ChatChannelKind.Team, key, "[Alice] hi"));
    }

    [Fact]
    public void Two_identical_records_consume_independently()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(ChatChannelKind.Team, key, "[Alice] hi");
        buffer.Record(ChatChannelKind.Team, key, "[Alice] hi");

        Assert.True(buffer.TryConsume(ChatChannelKind.Team, key, "[Alice] hi"));
        Assert.True(buffer.TryConsume(ChatChannelKind.Team, key, "[Alice] hi"));
        Assert.False(buffer.TryConsume(ChatChannelKind.Team, key, "[Alice] hi"));
    }

    [Fact]
    public void A_clan_echo_does_not_consume_an_identical_team_entry()
    {
        var (buffer, _) = Build();
        var key = (1UL, Guid.NewGuid());

        buffer.Record(ChatChannelKind.Team, key, "[dave] hello");

        Assert.False(buffer.TryConsume(ChatChannelKind.Clan, key, "[dave] hello"));
        Assert.True(buffer.TryConsume(ChatChannelKind.Team, key, "[dave] hello"));
    }

    [Fact]
    public void Consumes_a_clan_entry_recorded_for_the_same_key()
    {
        var (buffer, _) = Build();
        var key = (1UL, Guid.NewGuid());

        buffer.Record(ChatChannelKind.Clan, key, "[dave] hello");

        Assert.True(buffer.TryConsume(ChatChannelKind.Clan, key, "[dave] hello"));
    }
}
