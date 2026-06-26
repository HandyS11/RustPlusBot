using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class EmbeddedItemNameResolverTests
{
    [Fact]
    public void Resolve_KnownId_ReturnsDisplayName()
    {
        // -151838493 is "Wood" — present in the 4-item seed bundle.
        var resolver = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());
        Assert.Equal("Wood", resolver.Resolve(-151838493));
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsFallback()
    {
        var resolver = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());
        Assert.Equal("Item 123456789", resolver.Resolve(123456789));
    }
}
