using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerServerTests
{
    [Fact]
    public async Task ReconcileServer_CreatesCategoryNamedAfterServer_ScopedToServer()
    {
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0)
            .WithMessage(WorkspaceScope.PerServer, "server.info", "info", "info");
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.1.1.1", Port = 28015 });
        var sut = harness.Build();

        var result = await sut.ReconcileServerAsync(1, serverId);

        Assert.Equal(ReconcileStatus.Provisioned, result.Status);
        var category = await harness.Store.GetCategoryAsync(1, serverId);
        Assert.NotNull(category);
        Assert.Equal(category!.DiscordCategoryId, await harness.Gateway.FindCategoryAsync(1, "Rustopia EU", default));
        var channels = await harness.Store.GetChannelsAsync(1, serverId);
        Assert.Single(channels);
        Assert.Equal(serverId, channels[0].RustServerId);
        Assert.Empty(await harness.Store.GetChannelsAsync(1, null)); // global scope untouched
    }

    [Fact]
    public async Task ReconcileServer_UnknownServer_IsNoOp()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0);
        harness.Servers.GetAsync(1, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((RustServer?)null);
        var sut = harness.Build();

        var result = await sut.ReconcileServerAsync(1, Guid.NewGuid());

        Assert.Equal(ReconcileStatus.Skipped, result.Status);
        Assert.Equal(0, harness.Gateway.CreatedCategories);
    }
}
