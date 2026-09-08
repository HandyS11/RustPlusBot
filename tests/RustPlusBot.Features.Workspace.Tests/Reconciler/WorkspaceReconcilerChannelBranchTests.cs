using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>
/// Characterisation tests pinning the channel-reconcile branches the other suites leave open: renaming a
/// still-live channel rather than replacing it, and skipping the reorder call outright when a category
/// holds fewer than two provisioned channels.
/// </summary>
public sealed class WorkspaceReconcilerChannelBranchTests
{
    [Fact]
    public async Task Culture_change_renames_the_live_channel_instead_of_recreating_it()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var original = (await harness.Store.GetChannelsAsync(1, null))[0].DiscordChannelId;
        await harness.Store.SetCultureAsync(1, "fr");

        await sut.ReconcileGlobalAsync(1);

        var after = (await harness.Store.GetChannelsAsync(1, null))[0].DiscordChannelId;
        Assert.Equal(original, after); // same channel, settings applied in place
        Assert.Equal(1, harness.Gateway.CreatedChannels);
        var categoryId = Assert.Single(harness.Gateway.CategoryIds);
        Assert.Equal(original, await harness.Gateway.FindChannelAsync(1, categoryId, "informations", default));
    }

    [Fact]
    public async Task A_category_with_one_channel_never_asks_the_gateway_to_order_it()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        Assert.Single(harness.Gateway.ChannelIds);
        Assert.Equal(0, harness.Gateway.EnsureOrderCalls);
    }

    [Fact]
    public async Task A_category_with_two_channels_asks_the_gateway_to_order_it_every_pass()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 1);
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.EnsureOrderCalls);
        Assert.Equal(0, harness.Gateway.ReorderCalls); // created in order already: no move issued
    }
}
