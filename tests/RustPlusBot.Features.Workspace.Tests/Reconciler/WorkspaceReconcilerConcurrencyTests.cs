using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerConcurrencyTests
{
    [Fact]
    public async Task ConcurrentReconciles_CreateOneCategory()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();

        await Task.WhenAll(sut.ReconcileGlobalAsync(1), sut.ReconcileGlobalAsync(1));

        Assert.Equal(1, harness.Gateway.CreatedCategories);
        Assert.Single(harness.Gateway.CategoryIds);
        Assert.Single(harness.Gateway.ChannelIds);
    }
}
