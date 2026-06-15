using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingHandlerTests
{
    private static ICredentialProtector PassThrough()
    {
        var p = Substitute.For<ICredentialProtector>();
        p.Protect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        return p;
    }

    private static PairingHandler CreateHandler(BotDbContext context, IEventBus bus) =>
        new(new ServerService(context), new CredentialStore(context, PassThrough()), bus,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PairingHandler>.Instance);

    private static PairingNotification ServerPairing(string ip = "1.2.3.4", int port = 28015, ulong steam = 7UL) =>
        new(PairingKind.Server, "Rustopia", ip, port, steam, "ptoken");

    [Fact]
    public async Task ServerPairing_CreatesServerCredentialAndFiresEventOnce()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        var server = await context.RustServers.SingleAsync();
        Assert.Equal("Rustopia", server.Name);
        var credential = await context.PlayerCredentials.SingleAsync();
        Assert.Equal(server.Id, credential.RustServerId);
        Assert.Equal(CredentialStatus.Active, credential.Status);
        await bus.Received(1).PublishAsync(
            Arg.Is<ServerRegisteredEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondOwnerSameServer_AddsStandbyCredentialNoEvent()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 1UL, ServerPairing(steam: 1UL), CancellationToken.None);
        bus.ClearReceivedCalls();
        await handler.HandleAsync(10UL, 2UL, ServerPairing(steam: 2UL), CancellationToken.None);

        Assert.Single(await context.RustServers.ToListAsync());
        Assert.Equal(2, await context.PlayerCredentials.CountAsync());
        var second = await context.PlayerCredentials.SingleAsync(c => c.OwnerUserId == 2UL);
        Assert.Equal(CredentialStatus.Standby, second.Status);
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntityPairing_IsIgnored()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 1UL,
            new PairingNotification(PairingKind.Entity, "x", "1.2.3.4", 28015, 1UL, "t"), CancellationToken.None);

        Assert.Empty(await context.RustServers.ToListAsync());
        Assert.Empty(await context.PlayerCredentials.ToListAsync());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }
}
