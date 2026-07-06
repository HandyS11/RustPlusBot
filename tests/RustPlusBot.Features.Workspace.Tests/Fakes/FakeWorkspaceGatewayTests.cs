using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Fakes;

public sealed class FakeWorkspaceGatewayTests
{
    [Fact]
    public async Task CreateAndFind_RoundTrips()
    {
        var gateway = new FakeWorkspaceGateway();
        var categoryId = await gateway.CreateCategoryAsync(1, "RustPlusBot", default);
        Assert.True(gateway.CategoryExists(1, categoryId));
        Assert.Equal(categoryId, await gateway.FindCategoryAsync(1, "rustplusbot", default));

        var channelId =
            await gateway.CreateChannelAsync(1, categoryId, "information", ChannelPermissionProfile.ReadOnly, default);
        Assert.Equal(channelId, await gateway.FindChannelAsync(1, categoryId, "information", default));

        gateway.ExternallyDeleteChannel(channelId);
        Assert.False(gateway.ChannelExists(1, channelId));
    }
}
