using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class TeamChatInboundProcessorTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private static (TeamChatInboundProcessor Processor, IChatSender Sender, RelayDedupBuffer Dedup, IMuteStore Mute)
        Build(ChatSendResult sendResult = ChatSendResult.Sent)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var sender = Substitute.For<IChatSender>();
        sender.SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(sendResult);
        var locator = Substitute.For<IChatChannelLocator>();
        locator.ResolveAsync(777UL, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(10UL, ServerId));
        locator.ResolveAsync(Arg.Is<ulong>(c => c != 777UL), Arg.Any<CancellationToken>())
            .Returns(((ulong, Guid)?)null);
        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetMutedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        scopeProvider.GetService(typeof(IMuteStore)).Returns(muteStore);
        scope.ServiceProvider.Returns(scopeProvider);
        scopeFactory.CreateScope().Returns(scope);
        var processor = new TeamChatInboundProcessor([locator], sender, dedup, scopeFactory);
        return (processor, sender, dedup, muteStore);
    }

    [Fact]
    public async Task Ignores_bot_or_webhook_authors()
    {
        var (processor, sender, _, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: true, 777UL, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_non_teamchat_channels()
    {
        var (processor, sender, _, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 555UL, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_empty_content()
    {
        var (processor, sender, _, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "   ");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Formats_records_and_sends()
    {
        var (processor, sender, dedup, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Sent, outcome);
        await sender.Received(1)
            .SendAsync(ChatChannelKind.Team, 10UL, ServerId, "[Alice] hello", Arg.Any<CancellationToken>());
        Assert.True(dedup.TryConsume(ChatChannelKind.Team, (10UL, ServerId), "[Alice] hello"));
    }

    [Fact]
    public async Task Reports_failed_when_not_connected()
    {
        var (processor, _, _, _) = Build(ChatSendResult.NotConnected);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Failed, outcome);
    }

    [Fact]
    public async Task Ignores_muted_server()
    {
        var (processor, sender, _, mute) = Build();
        mute.GetMutedAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(true);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
