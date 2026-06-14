using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerAdoptTests
{
    [Fact]
    public async Task StoredChannelGone_ButSameNamedExists_RebindsNotRecreates()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        // Simulate: the channel row still points at an id that no longer exists, but a same-named
        // channel exists under the category (e.g. recreated out of band).
        var channel = (await harness.Store.GetChannelsAsync(1, null))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);
        var category = await harness.Store.GetCategoryAsync(1, null);
        await harness.Gateway.CreateChannelAsync(1, category!.DiscordCategoryId, "information", ChannelPermissionProfile.ReadOnly, default);
        var createdBefore = harness.Gateway.CreatedChannels;

        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(createdBefore, harness.Gateway.CreatedChannels); // adopted, not created
        var rebound = (await harness.Store.GetChannelsAsync(1, null))[0];
        Assert.True(harness.Gateway.ChannelExists(1, rebound.DiscordChannelId));
    }
}
