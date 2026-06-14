using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Teardown;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerHealTests
{
    [Fact]
    public async Task HealGuild_HealsPerServerScope_EvenWithoutGlobal()
    {
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0);
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.1.1.1", Port = 1 });
        var sut = harness.Build();
        await sut.ReconcileServerAsync(1, serverId);

        var channel = (await harness.Store.GetChannelsAsync(1, serverId))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);

        await sut.HealGuildAsync(1);

        Assert.True(harness.Gateway.ChannelExists(1, (await harness.Store.GetChannelsAsync(1, serverId))[0].DiscordChannelId));
    }

    [Fact]
    public async Task HealGuild_RecreatesDeletedChannel_WhenProvisioned()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var channel = (await harness.Store.GetChannelsAsync(1, null))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);

        await sut.HealGuildAsync(1);

        Assert.True(harness.Gateway.ChannelExists(1, (await harness.Store.GetChannelsAsync(1, null))[0].DiscordChannelId));
    }

    [Fact]
    public async Task HealGuild_DoesNotResurrect_AfterReset()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var teardown = new WorkspaceTeardownService(harness.Gateway, harness.Store, new ProvisioningLock());
        await teardown.ResetGuildAsync(1);

        await sut.HealGuildAsync(1);

        Assert.Empty(harness.Gateway.CategoryIds);
        Assert.Empty(harness.Gateway.ChannelIds);
        Assert.Null(await harness.Store.GetCategoryAsync(1, null));
    }
}
