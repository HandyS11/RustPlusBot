using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemDatabaseNameResolverTests
{
    private readonly IItemNameResolver _resolver = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void Resolve_ReturnsName_ForKnownId()
    {
        Assert.Equal("Assault Rifle", _resolver.Resolve(1545779598));
    }

    [Fact]
    public void Resolve_FallsBack_ForUnknownId()
    {
        Assert.Equal("Item 999999", _resolver.Resolve(999999));
    }
}
