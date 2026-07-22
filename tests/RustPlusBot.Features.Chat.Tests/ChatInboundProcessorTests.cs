using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class ChatInboundProcessorTests
{
    private const ulong TeamChannel = 777UL;
    private const ulong ClanChannel = 888UL;
    private const ulong UnclaimedChannel = 555UL;

    private static readonly Guid ServerId = Guid.NewGuid();

    private static ulong ChannelFor(ChatChannelKind kind) => kind == ChatChannelKind.Clan ? ClanChannel : TeamChannel;

    private static (ChatInboundProcessor Processor, IChatSender Sender, RelayDedupBuffer Dedup, IMuteStore Mute)
        Build(ChatSendResult sendResult = ChatSendResult.Sent)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var sender = Substitute.For<IChatSender>();
        sender.SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(sendResult);

        // Both doubles MUST have Kind stubbed explicitly: NSubstitute returns ChatChannelKind.Team (enum 0)
        // by default, so an unstubbed clan double would silently behave as a second team locator.
        var team = Substitute.For<IChatChannelLocator>();
        team.Kind.Returns(ChatChannelKind.Team);
        team.ResolveAsync(TeamChannel, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(10UL, ServerId));
        team.ResolveAsync(Arg.Is<ulong>(c => c != TeamChannel), Arg.Any<CancellationToken>())
            .Returns(((ulong, Guid)?)null);
        var clan = Substitute.For<IChatChannelLocator>();
        clan.Kind.Returns(ChatChannelKind.Clan);
        clan.ResolveAsync(ClanChannel, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(10UL, ServerId));
        clan.ResolveAsync(Arg.Is<ulong>(c => c != ClanChannel), Arg.Any<CancellationToken>())
            .Returns(((ulong, Guid)?)null);

        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetMutedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        scopeProvider.GetService(typeof(IMuteStore)).Returns(muteStore);
        scope.ServiceProvider.Returns(scopeProvider);
        scopeFactory.CreateScope().Returns(scope);
        var processor = new ChatInboundProcessor([team, clan], sender, dedup, scopeFactory);
        return (processor, sender, dedup, muteStore);
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Ignores_bot_or_webhook_authors(ChatChannelKind kind)
    {
        var (processor, sender, _, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: true, ChannelFor(kind), "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Ignores_empty_content(ChatChannelKind kind)
    {
        var (processor, sender, _, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, ChannelFor(kind), "Alice", "   ");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Formats_records_and_sends(ChatChannelKind kind)
    {
        var (processor, sender, dedup, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, ChannelFor(kind), "dave", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Sent, outcome);
        await sender.Received(1).SendAsync(kind, 10UL, ServerId, "[dave] hello", Arg.Any<CancellationToken>());

        // The dedup entry must be recorded under the SAME kind the line was sent on, or the in-game echo
        // is never suppressed and every relayed line is re-posted to Discord.
        Assert.True(dedup.TryConsume(kind, (10UL, ServerId), "[dave] hello"));
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Does_not_record_a_dedup_entry_under_the_other_kind(ChatChannelKind kind)
    {
        var (processor, _, dedup, _) = Build();
        var other = kind == ChatChannelKind.Clan ? ChatChannelKind.Team : ChatChannelKind.Clan;
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, ChannelFor(kind), "dave", "hello");

        await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.False(dedup.TryConsume(other, (10UL, ServerId), "[dave] hello"));
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Reports_failed_when_the_send_fails(ChatChannelKind kind)
    {
        var (processor, _, _, _) = Build(ChatSendResult.Failed);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, ChannelFor(kind), "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Failed, outcome);
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Reports_failed_when_not_connected(ChatChannelKind kind)
    {
        var (processor, _, _, _) = Build(ChatSendResult.NotConnected);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, ChannelFor(kind), "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Failed, outcome);
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Ignores_muted_server_without_recording_dedup(ChatChannelKind kind)
    {
        var (processor, sender, dedup, mute) = Build();
        mute.GetMutedAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(true);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, ChannelFor(kind), "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.False(dedup.TryConsume(kind, (10UL, ServerId), "[Alice] hello"));
    }

    [Fact]
    public async Task Routes_an_inbound_message_by_which_locator_claims_the_channel()
    {
        var (processor, sender, dedup, _) = Build();

        var teamOutcome = await processor.ProcessAsync(
            new InboundMessage(AuthorIsBotOrWebhook: false, TeamChannel, "dave", "hi team"), CancellationToken.None);
        var clanOutcome = await processor.ProcessAsync(
            new InboundMessage(AuthorIsBotOrWebhook: false, ClanChannel, "dave", "hi clan"), CancellationToken.None);

        Assert.Equal(InboundOutcome.Sent, teamOutcome);
        Assert.Equal(InboundOutcome.Sent, clanOutcome);
        await sender.Received(1)
            .SendAsync(ChatChannelKind.Team, 10UL, ServerId, "[dave] hi team", Arg.Any<CancellationToken>());
        await sender.Received(1)
            .SendAsync(ChatChannelKind.Clan, 10UL, ServerId, "[dave] hi clan", Arg.Any<CancellationToken>());
        Assert.True(dedup.TryConsume(ChatChannelKind.Team, (10UL, ServerId), "[dave] hi team"));
        Assert.True(dedup.TryConsume(ChatChannelKind.Clan, (10UL, ServerId), "[dave] hi clan"));
    }

    [Fact]
    public async Task Ignores_an_inbound_message_in_a_channel_no_locator_claims()
    {
        var (processor, sender, _, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, UnclaimedChannel, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
