using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence;
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
    public async Task PurgeServer_TearsDownTheScope_BeforeDeletingTheRow()
    {
        var (sut, servers, store, _) = NewHarness();
        var serverId = Guid.NewGuid();
        servers.RemoveAsync(GuildId, serverId, Arg.Any<CancellationToken>()).Returns(true);

        var removed = await sut.RemoveServerAsync(GuildId, serverId);

        Assert.True(removed);
#pragma warning disable VSTHRD110 // Received.InOrder requires unawaited calls inside its synchronous ordering lambda.
        Received.InOrder(() =>
        {
            store.DeleteScopeAsync(GuildId, serverId, Arg.Any<CancellationToken>());
            servers.RemoveAsync(GuildId, serverId, Arg.Any<CancellationToken>());
        });
#pragma warning restore VSTHRD110
    }

    /// <summary>
    /// Over a real database, not a substituted store: ProvisionedCategories/Channels/Messages all declare
    /// ON DELETE CASCADE against RustServers, so deleting the server row first silently takes the
    /// provisioning records with it and teardown then has no channel ids left to delete — the Discord
    /// channels survive as orphans. Teardown must read those records while they still exist.
    /// </summary>
    [Fact]
    public async Task PurgeServer_DeletesTheDiscordChannels_EvenThoughTheRecordsCascadeWithTheRow()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var _ = connection;
        var options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options;
        await using var context = new BotDbContext(options);
        await context.Database.MigrateAsync();

        var server = new RustServer
        {
            GuildId = GuildId, Name = "srv", Ip = "1.2.3.4", Port = 28082
        };
        context.RustServers.Add(server);
        context.ProvisionedCategories.Add(new ProvisionedCategory
        {
            GuildId = GuildId, RustServerId = server.Id, DiscordCategoryId = 900
        });
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = GuildId, RustServerId = server.Id, ChannelKey = "info", DiscordChannelId = 901
        });
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = GuildId, RustServerId = server.Id, ChannelKey = "events", DiscordChannelId = 902
        });
        await context.SaveChangesAsync();

        var gateway = Substitute.For<IWorkspaceGateway>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var provisioningLock = new ProvisioningLock();
        var teardown = new WorkspaceTeardownService(gateway, new WorkspaceStore(context, clock), provisioningLock);
        var sut = new ServerPurgeService(new ServerService(context), teardown, provisioningLock);

        var removed = await sut.RemoveServerAsync(GuildId, server.Id);

        Assert.True(removed);
        await gateway.Received(1).DeleteChannelAsync(GuildId, 901UL, Arg.Any<CancellationToken>());
        await gateway.Received(1).DeleteChannelAsync(GuildId, 902UL, Arg.Any<CancellationToken>());
        await gateway.Received(1).DeleteCategoryAsync(GuildId, 900UL, Arg.Any<CancellationToken>());
        Assert.Empty(await context.RustServers.ToListAsync());
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
