using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class CctvBundleTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();

    [Fact]
    public void Bundle_LoadsAtLeastEightMonuments() =>
        Assert.True(_db.CctvMonuments.Count >= 8);

    [Fact]
    public void Bundle_HasCctvProvenanceDate() =>
        Assert.Equal(new DateOnly(2025, 11, 12), _db.Sources.CctvAsOf);
}
