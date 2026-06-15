using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class TeamChatRelayTests
{
    private static (TeamChatRelay Relay, ITeamChatWebhookPoster Poster, RelayDedupBuffer Dedup, ITeamChatChannelLocator
        Locator)
        Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var poster = Substitute.For<ITeamChatWebhookPoster>();
        var locator = Substitute.For<ITeamChatChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)777UL);
        var relay = new TeamChatRelay(locator, poster, dedup);
        return (relay, poster, dedup, locator);
    }

    [Fact]
    public async Task Posts_a_normal_message_via_webhook()
    {
        var (relay, poster, _, _) = Build();
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 999UL, "Bob", "hello", FromActivePlayer: false);

        await relay.RelayAsync(evt, CancellationToken.None);

        await poster.Received(1).PostAsync(777UL, "Bob", "hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drops_our_own_echo()
    {
        var (relay, poster, dedup, _) = Build();
        var key = (10UL, Guid.Empty);
        dedup.Record(key, "[Alice] hello");
        var echo = new TeamMessageReceivedEvent(10UL, Guid.Empty, 555UL, "BotPlayer", "[Alice] hello",
            FromActivePlayer: true);

        await relay.RelayAsync(echo, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_active_player_message_that_is_not_an_echo()
    {
        var (relay, poster, _, _) = Build();
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 555UL, "BotPlayer", "genuine", FromActivePlayer: true);

        await relay.RelayAsync(evt, CancellationToken.None);

        await poster.Received(1).PostAsync(777UL, "BotPlayer", "genuine", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_when_channel_not_provisioned()
    {
        var (relay, poster, _, locator) = Build();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 999UL, "Bob", "hello", FromActivePlayer: false);

        await relay.RelayAsync(evt, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
