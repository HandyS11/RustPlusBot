using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Servers;

namespace RustPlusBot.Features.Commands.Tests.Servers;

public sealed class ServerQueryServiceTests
{
    [Fact]
    public async Task RunsMatchingHandler_AndReturnsReply()
    {
        var handler = new StubHandler("pop", "Pop: 5/100 (0 queued)");
        var service = new ServerQueryService([handler]);
        var server = Guid.NewGuid();

        var reply = await service.RunAsync("pop", 1UL, server, "en", CancellationToken.None);

        Assert.Equal("Pop: 5/100 (0 queued)", reply);
        Assert.NotNull(handler.Seen);
        Assert.Equal(1UL, handler.Seen!.GuildId);
        Assert.Equal(server, handler.Seen.ServerId);
        Assert.Equal("en", handler.Seen.Culture);
        Assert.Empty(handler.Seen.Args);
        Assert.Equal(0UL, handler.Seen.SenderSteamId);
        Assert.Equal(string.Empty, handler.Seen.SenderName);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoHandlerMatches()
    {
        var service = new ServerQueryService([new StubHandler("pop", "x")]);

        var reply = await service.RunAsync("time", 1UL, Guid.NewGuid(), "en", CancellationToken.None);

        Assert.Null(reply);
    }

    private sealed class StubHandler(string name, string? reply) : ICommandHandler
    {
        public CommandContext? Seen { get; private set; }
        public string Name => name;

        public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Seen = context;
            return Task.FromResult(reply);
        }
    }
}
