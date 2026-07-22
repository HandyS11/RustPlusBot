using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Tests.Hosting;

public sealed class CommandsHostedServiceTests
{
    private static Harness Create(bool prefixThrows = false)
    {
        var sender = Substitute.For<IBotTeamChatSender>();
        sender.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ChatSendResult.Sent);

        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetMutedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        if (prefixThrows)
        {
            muteStore.When(m => m.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()))
                .Do(_ => throw new InvalidOperationException("dispatch boom"));
        }
        else
        {
            muteStore.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("!");
        }

        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var handler = new StubHandler("pop");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var cooldown = new CommandCooldown(clock, Options.Create(new CommandOptions()));
        var dispatcher = new CommandDispatcher(
            [handler], cooldown, muteStore, workspace, sender, NullLogger<CommandDispatcher>.Instance);

        var services = new ServiceCollection();
        services.AddScoped(_ => dispatcher);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var bus = new InMemoryEventBus();
        var service = new CommandsHostedService(bus, scopeFactory, NullLogger<CommandsHostedService>.Instance);

        return new Harness(service, bus, sender, handler, muteStore);
    }

    [Fact]
    public async Task TeamMessageReceivedEvent_dispatches_the_command_and_relays_the_reply()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.Sender.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IBotTeamChatSender.SendAsync)))
        {
            await h.Bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, serverId, 7UL, "alice", "!pop", FromActivePlayer: false));
            await Task.Delay(20);
        }

        Assert.True(h.Handler.Calls >= 1);
        await h.Sender.Received().SendAsync(10UL, serverId, "reply", Arg.Any<CancellationToken>());

        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task StartAsync_then_StopAsync_completes_cleanly()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        var stop = h.Service.StopAsync(default);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DispatchLoop_survives_a_faulting_dispatch_and_StopAsync_completes_cleanly()
    {
        var h = Create(prefixThrows: true);
        await h.Service.StartAsync(default);

        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !h.MuteStore.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IMuteStore.GetPrefixAsync)))
        {
            await h.Bus.PublishAsync(
                new TeamMessageReceivedEvent(10UL, serverId, 7UL, "alice", "!pop", FromActivePlayer: false));
            await Task.Delay(20);
        }

        // The dispatch threw; the per-event catch swallowed it (LogDispatchFaulted) so the loop keeps running.
        await h.MuteStore.Received().GetPrefixAsync(10UL, serverId, Arg.Any<CancellationToken>());
        await h.Sender.DidNotReceive().SendAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    private sealed record Harness(
        CommandsHostedService Service,
        InMemoryEventBus Bus,
        IBotTeamChatSender Sender,
        StubHandler Handler,
        IMuteStore MuteStore);

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
