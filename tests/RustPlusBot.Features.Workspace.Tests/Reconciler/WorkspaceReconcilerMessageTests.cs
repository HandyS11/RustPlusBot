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
}
