using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class EmbeddedItemDatabaseTests
{
    private readonly EmbeddedItemDatabase _db = new();

    [Fact]
    public void GetById_ReturnsKnownItem()
    {
        var ak = _db.GetById(1545779598);
        Assert.NotNull(ak);
        Assert.Equal("AK-47", ak!.Name);
    }

    [Fact]
    public void GetById_ReturnsNullForUnknown()
    {
        Assert.Null(_db.GetById(123456789));
    }

    [Fact]
    public void Sources_ArePopulated()
    {
        Assert.Equal(new DateOnly(2026, 4, 8), _db.Sources.NamesAsOf);
    }
}
