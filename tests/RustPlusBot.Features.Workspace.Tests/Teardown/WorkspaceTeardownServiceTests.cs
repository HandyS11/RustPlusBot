using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Features.Workspace.Tests.Reconciler;

namespace RustPlusBot.Features.Workspace.Tests.Teardown;

public sealed class WorkspaceTeardownServiceTests
{
    [Fact]
    public async Task ResetGuild_DeletesChannelsCategoriesAndRecords()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        await harness.Build().ReconcileGlobalAsync(1);

        var teardown = new WorkspaceTeardownService(harness.Gateway, harness.Store, new ProvisioningLock());
        await teardown.ResetGuildAsync(1);

        Assert.Empty(harness.Gateway.ChannelIds);
        Assert.Empty(harness.Gateway.CategoryIds);
        Assert.Null(await harness.Store.GetCategoryAsync(1, null));
        Assert.Empty(await harness.Store.GetChannelsAsync(1, null));
    }

    [Fact]
    public async Task RemoveServer_DeletesOnlyThatServerScope()
    {
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0);
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.1.1.1", Port = 1 });
        var reconciler = harness.Build();
        await reconciler.ReconcileGlobalAsync(1);
        await reconciler.ReconcileServerAsync(1, serverId);

        var teardown = new WorkspaceTeardownService(harness.Gateway, harness.Store, new ProvisioningLock());
        await teardown.RemoveServerAsync(1, serverId);

        Assert.Null(await harness.Store.GetCategoryAsync(1, serverId));
        Assert.NotNull(await harness.Store.GetCategoryAsync(1, null)); // global retained
    }
}
