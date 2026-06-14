using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerProvisionTests
{
    private static ReconcilerHarness GlobalHarness() => new ReconcilerHarness()
        .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
        .WithChannel(WorkspaceScope.Global, "setup", "channel.setup.name", 1)
        .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 2)
        .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");

    [Fact]
    public async Task ReconcileGlobal_FreshGuild_CreatesCategoryChannelsAndMessage()
    {
        var harness = GlobalHarness();
        var sut = harness.Build();

        var result = await sut.ReconcileGlobalAsync(1);

        Assert.Equal(ReconcileStatus.Provisioned, result.Status);
        Assert.Equal(1, harness.Gateway.CreatedCategories);
        Assert.Equal(3, harness.Gateway.CreatedChannels);
        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.NotNull(await harness.Store.GetCategoryAsync(1, null));
        Assert.Equal(3, (await harness.Store.GetChannelsAsync(1, null)).Count);
        Assert.NotNull(await harness.Store.GetMessageAsync(1, null, "information.main"));
    }

    [Fact]
    public async Task ReconcileGlobal_MissingBotPermissions_DoesNothing()
    {
        var harness = GlobalHarness();
        harness.Gateway.MissingPermissions = ["Manage Channels"];
        var sut = harness.Build();

        var result = await sut.ReconcileGlobalAsync(1);

        Assert.Equal(ReconcileStatus.MissingPermissions, result.Status);
        Assert.Equal(["Manage Channels"], result.MissingPermissions);
        Assert.Equal(0, harness.Gateway.CreatedCategories);
        Assert.Equal(0, harness.Gateway.CreatedChannels);
    }
}
