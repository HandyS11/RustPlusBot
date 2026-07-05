using NSubstitute;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class BotTeamChatSenderTests
{
    [Fact]
    public async Task Prefixes_the_message_and_forwards()
    {
        var inner = Substitute.For<ITeamChatSender>();
        inner.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TeamChatSendResult.Sent);
        var serverId = Guid.NewGuid();
        var sut = new BotTeamChatSender(inner);

        var result = await sut.SendAsync(10UL, serverId, "hello", CancellationToken.None);

        Assert.Equal(TeamChatSendResult.Sent, result);
        await inner.Received(1).SendAsync(10UL, serverId, "[R+] hello", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TeamChatSendResult.Sent)]
    [InlineData(TeamChatSendResult.NotConnected)]
    [InlineData(TeamChatSendResult.Failed)]
    public async Task Passes_the_result_through(TeamChatSendResult expected)
    {
        var inner = Substitute.For<ITeamChatSender>();
        inner.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var sut = new BotTeamChatSender(inner);

        var result = await sut.SendAsync(1UL, Guid.Empty, "x", CancellationToken.None);

        Assert.Equal(expected, result);
    }
}
