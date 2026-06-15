using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class TeamChatInboundProcessorTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private static (TeamChatInboundProcessor Processor, ITeamChatSender Sender, RelayDedupBuffer Dedup) Build(
        TeamChatSendResult sendResult = TeamChatSendResult.Sent)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var sender = Substitute.For<ITeamChatSender>();
        sender.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(sendResult);
        var locator = Substitute.For<ITeamChatChannelLocator>();
        locator.ResolveAsync(777UL, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(10UL, ServerId));
        locator.ResolveAsync(Arg.Is<ulong>(c => c != 777UL), Arg.Any<CancellationToken>())
            .Returns(((ulong, Guid)?)null);
        var processor = new TeamChatInboundProcessor(locator, sender, dedup);
        return (processor, sender, dedup);
    }

    [Fact]
    public async Task Ignores_bot_or_webhook_authors()
    {
        var (processor, sender, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: true, 777UL, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_non_teamchat_channels()
    {
        var (processor, sender, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 555UL, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_empty_content()
    {
        var (processor, sender, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "   ");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Formats_records_and_sends()
    {
        var (processor, sender, dedup) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Sent, outcome);
        await sender.Received(1).SendAsync(10UL, ServerId, "[Alice] hello", Arg.Any<CancellationToken>());
        Assert.True(dedup.TryConsume((10UL, ServerId), "[Alice] hello"));
    }

    [Fact]
    public async Task Reports_failed_when_not_connected()
    {
        var (processor, _, _) = Build(TeamChatSendResult.NotConnected);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Failed, outcome);
    }
}
