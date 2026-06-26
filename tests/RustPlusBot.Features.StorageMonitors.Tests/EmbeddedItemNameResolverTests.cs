using RustPlusBot.Features.StorageMonitors.Naming;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class EmbeddedItemNameResolverTests
{
    [Fact]
    public void Resolve_KnownId_ReturnsDisplayName()
    {
        var resolver = new EmbeddedItemNameResolver();
        Assert.Equal("Wood", resolver.Resolve(-151838493));
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsFallback()
    {
        var resolver = new EmbeddedItemNameResolver();
        Assert.Equal("Item 123456789", resolver.Resolve(123456789));
    }
}
