using NSubstitute;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Teardown;

public sealed class ServerPurgeServiceTests
{
    private const ulong GuildId = 1;

    private static (ServerPurgeService Sut, IServerService Servers, IWorkspaceStore Store, ProvisioningLock Lock)
        NewHarness()
    {
        var gateway = Substitute.For<IWorkspaceGateway>();
        var store = Substitute.For<IWorkspaceStore>();
        IReadOnlyList<ProvisionedChannel> noChannels = [];
        store.GetChannelsAsync(Arg.Any<ulong>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(noChannels);
        store.GetCategoryAsync(Arg.Any<ulong>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((ProvisionedCategory?)null);

        var provisioningLock = new ProvisioningLock();
        var servers = Substitute.For<IServerService>();
        var teardown = new WorkspaceTeardownService(gateway, store, provisioningLock);
        return (new ServerPurgeService(servers, teardown, provisioningLock), servers, store, provisioningLock);
    }

    /// <summary>
    /// The race this guards: the row delete and the Discord teardown must happen under one continuous hold of
    /// the guild's provisioning lock. If the lock is free while the row is being deleted, a reconcile that is
    /// already in flight can re-create the category and channels that teardown is about to remove, leaving
    /// orphaned Discord resources behind (and an FK violation when it writes its ProvisionedMessages row).
    /// </summary>
    [Fact]
    public async Task PurgeServer_HoldsProvisioningLock_WhileTheServerRowIsDeleted()
    {
        var (sut, servers, _, provisioningLock) = NewHarness();
        var serverId = Guid.NewGuid();
        var lockWasFreeDuringDelete = false;

        servers.RemoveAsync(GuildId, serverId, Arg.Any<CancellationToken>()).Returns(_ =>
            ProbeLockAsync());

        async Task<bool> ProbeLockAsync()
        {
            // If the purge holds the lock, this contending acquire can never succeed, so it must time out.
            using var probe = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            try
            {
                using var handle = await provisioningLock.AcquireAsync(GuildId, probe.Token);
                lockWasFreeDuringDelete = true;
            }
            catch (OperationCanceledException)
            {
                // Expected: the lock is held by the purge.
            }

            return true;
        }

        await sut.RemoveServerAsync(GuildId, serverId);

        Assert.False(lockWasFreeDuringDelete);
    }

    [Fact]
    public async Task PurgeServer_DeletesTheRow_ThenTearsDownTheScope()
    {
        var (sut, servers, store, _) = NewHarness();
        var serverId = Guid.NewGuid();
        servers.RemoveAsync(GuildId, serverId, Arg.Any<CancellationToken>()).Returns(true);

        var removed = await sut.RemoveServerAsync(GuildId, serverId);

        Assert.True(removed);
#pragma warning disable VSTHRD110 // Received.InOrder requires unawaited calls inside its synchronous ordering lambda.
        Received.InOrder(() =>
        {
            servers.RemoveAsync(GuildId, serverId, Arg.Any<CancellationToken>());
            store.DeleteScopeAsync(GuildId, serverId, Arg.Any<CancellationToken>());
        });
#pragma warning restore VSTHRD110
    }

    [Fact]
    public async Task PurgeServer_WhenRowAlreadyGone_StillTearsDown_AndReportsNotRemoved()
    {
        var (sut, servers, store, _) = NewHarness();
        var serverId = Guid.NewGuid();
        servers.RemoveAsync(GuildId, serverId, Arg.Any<CancellationToken>()).Returns(false);

        var removed = await sut.RemoveServerAsync(GuildId, serverId);

        Assert.False(removed);
        await store.Received(1).DeleteScopeAsync(GuildId, serverId, Arg.Any<CancellationToken>());
    }
}
