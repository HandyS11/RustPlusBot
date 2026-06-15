using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Pairing.Accounts;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class AccountDisconnectServiceTests
{
    private static readonly string[] RustopiaNames = ["Rustopia"];

    private static (AccountDisconnectService Sut, IPairingSupervisor Sup, IFcmRegistrationStore Regs,
        ICredentialStore Creds, IServerService Servers, IEventBus Bus) Create()
    {
        var sup = Substitute.For<IPairingSupervisor>();
        var regs = Substitute.For<IFcmRegistrationStore>();
        var creds = Substitute.For<ICredentialStore>();
        var servers = Substitute.For<IServerService>();
        var bus = Substitute.For<IEventBus>();
        return (new AccountDisconnectService(sup, regs, creds, servers, bus), sup, regs, creds, servers, bus);
    }

    [Fact]
    public async Task Disconnect_StopsListener_DisablesRegistration_RemovesCreds_PublishesPerServer()
    {
        var (sut, sup, regs, creds, _, bus) = Create();
        var regId = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new FcmRegistration
            {
                Id = regId, GuildId = 10UL, OwnerUserId = 99UL
            });
        creds.RemoveForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new List<Guid>
            {
                s1, s2
            });

        var count = await sut.DisconnectAsync(10UL, 99UL);

        Assert.Equal(2, count);
        await sup.Received(1).StopListenerAsync(10UL, 99UL);
        await regs.Received(1).SetStatusAsync(regId, FcmRegistrationStatus.Disabled, Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ServerCredentialsChangedEvent>(e => e.GuildId == 10UL && e.ServerId == s1),
            Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ServerCredentialsChangedEvent>(e => e.ServerId == s2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disconnect_WhenNothingStored_StopsListener_NoEvents_ReturnsZero()
    {
        var (sut, sup, regs, creds, _, bus) = Create();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns((FcmRegistration?)null);
        creds.RemoveForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        var count = await sut.DisconnectAsync(10UL, 99UL);

        Assert.Equal(0, count);
        await sup.Received(1).StopListenerAsync(10UL, 99UL);
        await regs.DidNotReceive().SetStatusAsync(Arg.Any<Guid>(), Arg.Any<FcmRegistrationStatus>(),
            Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerCredentialsChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_ReportsConnectedAndServerNames()
    {
        var (sut, _, regs, creds, servers, _) = Create();
        var s1 = Guid.NewGuid();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new FcmRegistration
            {
                Id = Guid.NewGuid(), GuildId = 10UL, OwnerUserId = 99UL
            });
        creds.ListServerIdsForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new List<Guid>
            {
                s1
            });
        servers.GetAsync(10UL, s1, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = s1,
                GuildId = 10UL,
                Name = "Rustopia",
                Ip = "1.1.1.1",
                Port = 28015
            });

        var preview = await sut.PreviewAsync(10UL, 99UL);

        Assert.True(preview.IsConnected);
        Assert.Equal(RustopiaNames, preview.AffectedServerNames);
    }

    [Fact]
    public async Task Preview_WhenNothing_ReportsNotConnected()
    {
        var (sut, _, regs, creds, _, _) = Create();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns((FcmRegistration?)null);
        creds.ListServerIdsForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        var preview = await sut.PreviewAsync(10UL, 99UL);

        Assert.False(preview.IsConnected);
        Assert.Empty(preview.AffectedServerNames);
    }
}
