using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerSelfHealTests
{
    [Fact]
    public async Task DeletedChannel_IsRecreatedOnReconcile()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var channel = (await harness.Store.GetChannelsAsync(1, null))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);

        await sut.ReconcileGlobalAsync(1);

        var healed = (await harness.Store.GetChannelsAsync(1, null))[0];
        Assert.True(harness.Gateway.ChannelExists(1, healed.DiscordChannelId));
        Assert.Equal(2, harness.Gateway.CreatedChannels); // original + heal (no same-named adopt available)
    }
}
