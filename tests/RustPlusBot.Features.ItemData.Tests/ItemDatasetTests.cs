using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemDatasetTests
{
    [Fact]
    public void ItemRecord_AllowsNullCalculatorData()
    {
        var record = new ItemRecord(1, "Wood", 1000, null, null, null, null);
        Assert.Equal("Wood", record.Name);
        Assert.Null(record.Recycle);
    }
}
