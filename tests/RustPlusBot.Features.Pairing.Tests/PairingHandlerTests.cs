using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Entities;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingHandlerTests
{
    private static readonly Guid FpServer = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
        new(PairingKind.Server, "Rustopia", ip, port, steam, "ptoken", FacepunchServerId: FpServer, EntityId: 0UL);

    private static PairingNotification EntityPairing(Guid fpServer, ulong entityId = 42UL) =>
        new(PairingKind.Entity, string.Empty, string.Empty, 0, 1UL, "t", FacepunchServerId: fpServer,
            EntityId: entityId);

    private static PairingNotification AlarmPairing(Guid fpServer, ulong entityId = 55UL) =>
        new(PairingKind.Entity, string.Empty, string.Empty, 0, 1UL, "t",
            FacepunchServerId: fpServer, EntityId: entityId, EntityKind: PairedEntityKind.SmartAlarm);

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
    public async Task ServerPairing_BackfillsFacepunchServerId()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        var server = await context.RustServers.SingleAsync();
        Assert.Equal(FpServer, server.FacepunchServerId);
    }

    [Fact]
    public async Task EntityPairing_KnownServer_PublishesSwitchPairedEvent()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);
        var server = await context.RustServers.SingleAsync();
        bus.ClearReceivedCalls();

        await handler.HandleAsync(10UL, 1UL, EntityPairing(FpServer, entityId: 42UL), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<SwitchPairedEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id && e.EntityId == 42UL),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntityPairing_UnknownServer_DropsAndCreatesNothing()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 1UL, EntityPairing(Guid.NewGuid(), 42UL), CancellationToken.None);

        Assert.Empty(await context.RustServers.ToListAsync());
        await bus.DidNotReceive().PublishAsync(Arg.Any<SwitchPairedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntityPairing_Alarm_PublishesAlarmPairedEvent_NotSwitch()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context; await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);
        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);
        var server = await context.RustServers.SingleAsync();
        bus.ClearReceivedCalls();

        await handler.HandleAsync(10UL, 1UL, AlarmPairing(FpServer, 55UL), CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<AlarmPairedEvent>(e => e.ServerId == server.Id && e.EntityId == 55UL), Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<SwitchPairedEvent>(), Arg.Any<CancellationToken>());
    }
}
