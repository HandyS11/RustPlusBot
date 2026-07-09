using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerMessageTests
{
    [Fact]
    public async Task DeletedMessage_IsReposted_NotEdited()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var message = await harness.Store.GetMessageAsync(1, null, "information.main");
        harness.Gateway.ExternallyDeleteMessage(message!.DiscordMessageId);

        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.PostedMessages); // reposted because the anchor was gone
    }

    [Fact]
    public async Task ChannelKeyRemovedFromRegistry_RetainsExistingRecord()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 1);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        // Re-run with a registry that no longer contains "settings", reusing the SAME store + gateway
        // so the prior "settings" record is still present.
        var sut2 = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .Build();

        await sut2.ReconcileGlobalAsync(1);

        // The orphaned "settings" channel record is retained, not deleted.
        var channels = await harness.Store.GetChannelsAsync(1, null);
        Assert.Contains(channels, c => c.ChannelKey == "settings");
    }

    [Fact]
    public async Task NewEarlierMessageBehindLiveOne_DeletesAndRepostsInDeclarationOrder()
    {
        // Simulates the #info map bug: "server.info" was already provisioned by a prior deploy, then
        // "server.info.map" is declared BEFORE it. Without reorder-repair the map would post below the
        // already-live status embed (Discord orders by creation time).
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0)
            .WithMessage(WorkspaceScope.PerServer, "server.info", "info", "info-text");
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId,
                GuildId = 1,
                Name = "Rustopia EU",
                Ip = "1.1.1.1",
                Port = 28015
            });
        var sut = harness.Build();
        await sut.ReconcileServerAsync(1, serverId);

        var originalInfoMessage = await harness.Store.GetMessageAsync(1, serverId, "server.info");
        Assert.NotNull(originalInfoMessage);
        var originalInfoMessageId = originalInfoMessage!.DiscordMessageId;
        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.Empty(harness.Gateway.DeletedMessageIds);

        // Next deploy: the registry now declares "server.info.map" BEFORE "server.info", but only the
        // latter has a provisioned record (reusing the same store + gateway).
        var sut2 = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0)
            .WithMessage(WorkspaceScope.PerServer, "server.info.map", "info", "map-text")
            .WithMessage(WorkspaceScope.PerServer, "server.info", "info", "info-text")
            .Build();

        var result = await sut2.ReconcileServerAsync(1, serverId);

        Assert.Equal(ReconcileStatus.Provisioned, result.Status);

        // The pre-existing "server.info" message was deleted exactly once.
        Assert.Single(harness.Gateway.DeletedMessageIds);
        Assert.Equal(originalInfoMessageId, harness.Gateway.DeletedMessageIds[0]);

        // Both messages were freshly posted this run (the prior run already posted one), map before info.
        Assert.Equal(3, harness.Gateway.PostedMessages);
        var repostedThisRun = harness.Gateway.PostedPayloads.TakeLast(2).ToList();
        Assert.Equal("map-text", repostedThisRun[0].Text);
        Assert.Equal("info-text", repostedThisRun[1].Text);

        // The persisted records now point at the freshly posted messages.
        var mapRecord = await harness.Store.GetMessageAsync(1, serverId, "server.info.map");
        var infoRecord = await harness.Store.GetMessageAsync(1, serverId, "server.info");
        Assert.NotNull(mapRecord);
        Assert.NotNull(infoRecord);
        Assert.NotEqual(originalInfoMessageId, infoRecord!.DiscordMessageId);
    }

    [Fact]
    public async Task ChannelAlreadyInDeclarationOrder_EditsInPlace_NoDelete()
    {
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0)
            .WithMessage(WorkspaceScope.PerServer, "server.info.map", "info", "map-text")
            .WithMessage(WorkspaceScope.PerServer, "server.info", "info", "info-text");
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = serverId,
                GuildId = 1,
                Name = "Rustopia EU",
                Ip = "1.1.1.1",
                Port = 28015
            });
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, serverId);
        Assert.Equal(2, harness.Gateway.PostedMessages);
        Assert.Empty(harness.Gateway.DeletedMessageIds);

        var result = await sut.ReconcileServerAsync(1, serverId);

        Assert.Equal(ReconcileStatus.Provisioned, result.Status);
        Assert.Empty(harness.Gateway.DeletedMessageIds); // already in order: no repair needed
        Assert.Equal(2, harness.Gateway.PostedMessages); // no reposts
        Assert.Equal(2, harness.Gateway.EditedMessages); // both edited in place
    }
}
