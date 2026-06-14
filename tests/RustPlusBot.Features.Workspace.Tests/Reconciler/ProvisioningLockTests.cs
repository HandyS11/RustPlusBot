using RustPlusBot.Features.Workspace.Reconciler;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class ProvisioningLockTests
{
    [Fact]
    public async Task SameGuild_SerializesHolders()
    {
        var sut = new ProvisioningLock();
        var handle = await sut.AcquireAsync(1);

        var second = sut.AcquireAsync(1);
        Assert.False(second.IsCompleted); // blocked while first is held

        handle.Dispose();
        var secondHandle = await second; // now succeeds
        secondHandle.Dispose();
    }

    [Fact]
    public async Task DifferentGuilds_DoNotBlock()
    {
        var sut = new ProvisioningLock();
        using var a = await sut.AcquireAsync(1);
        using var b = await sut.AcquireAsync(2); // does not block
        Assert.NotNull(b);
    }
}
