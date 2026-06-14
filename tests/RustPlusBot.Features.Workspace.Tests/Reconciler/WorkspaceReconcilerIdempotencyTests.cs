using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerIdempotencyTests
{
    private static ReconcilerHarness Harness() => new ReconcilerHarness()
        .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
        .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");

    [Fact]
    public async Task RunTwice_DoesNotDuplicate()
    {
        var harness = Harness();
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.CreatedCategories);
        Assert.Equal(1, harness.Gateway.CreatedChannels);
        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.Equal(1, harness.Gateway.EditedMessages); // second run edits the anchored message in place
        Assert.Single(harness.Gateway.CategoryIds);
        Assert.Single(harness.Gateway.ChannelIds);
    }
}
