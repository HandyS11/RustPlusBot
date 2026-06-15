using NSubstitute;
using RustPlusBot.Features.Connections.Removal;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ServerRemovalServiceTests
{
    [Fact]
    public async Task RemoveServer_Stops_Deletes_TearsDown_InOrder()
    {
        var serverId = Guid.NewGuid();
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var servers = Substitute.For<IServerService>();
        servers.RemoveAsync(10UL, serverId, Arg.Any<CancellationToken>()).Returns(true);
        var workspace = Substitute.For<IServerWorkspaceRemover>();
        var sut = new ServerRemovalService(supervisor, servers, workspace);

        var removed = await sut.RemoveServerAsync(10UL, serverId);

        Assert.True(removed);
#pragma warning disable VSTHRD110 // Received.InOrder requires unawaited calls inside its synchronous ordering lambda — this is the NSubstitute-prescribed pattern.
        Received.InOrder(() =>
        {
            supervisor.StopAsync(10UL, serverId);
            servers.RemoveAsync(10UL, serverId, Arg.Any<CancellationToken>());
            workspace.RemoveServerAsync(10UL, serverId, Arg.Any<CancellationToken>());
        });
#pragma warning restore VSTHRD110
    }

    [Fact]
    public async Task RemoveServer_WhenServerAbsent_ReturnsFalse_StillTearsDown()
    {
        var serverId = Guid.NewGuid();
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var servers = Substitute.For<IServerService>();
        servers.RemoveAsync(10UL, serverId, Arg.Any<CancellationToken>()).Returns(false);
        var workspace = Substitute.For<IServerWorkspaceRemover>();
        var sut = new ServerRemovalService(supervisor, servers, workspace);

        var removed = await sut.RemoveServerAsync(10UL, serverId);

        Assert.False(removed);
        await workspace.Received(1).RemoveServerAsync(10UL, serverId, Arg.Any<CancellationToken>());
    }
}
