using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Tests.Dispatching;

public sealed class CommandDispatcherTests
{
    private static (CommandDispatcher Sut, IBotTeamChatSender Sender, StubHandler Handler) Build(
        bool muted = false,
        string prefix = "!",
        string handlerName = "pop")
    {
        var sender = Substitute.For<IBotTeamChatSender>();
        var settings = Substitute.For<IMuteStore>();
        settings.GetMutedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(muted);
        settings.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(prefix);
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");
        var handler = new StubHandler(handlerName);
        var cooldown = new CommandCooldown(new TestClock(),
            Microsoft.Extensions.Options.Options.Create(new CommandOptions()));
        var sut = new CommandDispatcher([handler], cooldown, settings, workspace, sender,
            NullLogger<CommandDispatcher>.Instance);
        return (sut, sender, handler);
    }

    private static TeamMessageReceivedEvent Evt(string msg, bool fromActive = false) =>
        new(1, Guid.NewGuid(), 7, "alice", msg, fromActive);

    [Fact]
    public async Task RunsHandler_AndSendsReply()
    {
        var (sut, sender, handler) = Build();
        await sut.DispatchAsync(Evt("!pop"), CancellationToken.None);
        Assert.Equal(1, handler.Calls);
        await sender.Received(1).SendAsync(1, Arg.Any<Guid>(), "reply", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_FromActivePlayer()
    {
        var (sut, sender, handler) = Build();
        await sut.DispatchAsync(Evt("!pop", fromActive: true), CancellationToken.None);
        Assert.Equal(0, handler.Calls);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_NonCommandLine()
    {
        var (sut, _, handler) = Build();
        await sut.DispatchAsync(Evt("hello team"), CancellationToken.None);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Ignores_UnknownCommand()
    {
        var (sut, sender, handler) = Build();
        await sut.DispatchAsync(Evt("!nope"), CancellationToken.None);
        Assert.Equal(0, handler.Calls);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drops_WhenMuted_AndNotMuteCommand()
    {
        var (sut, sender, handler) = Build(muted: true); // handler is "pop"
        await sut.DispatchAsync(Evt("!pop"), CancellationToken.None);
        Assert.Equal(0, handler.Calls);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runs_MuteCommand_EvenWhenMuted()
    {
        var (sut, sender, handler) = Build(muted: true, handlerName: "mute");
        await sut.DispatchAsync(Evt("!mute"), CancellationToken.None);
        Assert.Equal(1, handler.Calls);
        await sender.Received(1).SendAsync(1, Arg.Any<Guid>(), "reply", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Honors_CustomPrefix()
    {
        var (sut, _, handler) = Build(prefix: ".");
        await sut.DispatchAsync(Evt(".pop"), CancellationToken.None);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cooldown_DropsSecondCall()
    {
        var (sut, _, handler) = Build();
        var evt = Evt("!pop");
        await sut.DispatchAsync(evt, CancellationToken.None);
        await sut.DispatchAsync(evt, CancellationToken.None); // same server+command within window
        Assert.Equal(1, handler.Calls);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class StubHandler(string name) : ICommandHandler
    {
        public int Calls { get; private set; }
        public string Name => name;

        public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<string?>("reply");
        }
    }
}
