using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemDatasetTests
{
    [Fact]
    public void ItemRecord_AllowsNullCalculatorData()
    {
        var record = new ItemRecord(1, "Wood", 1000, null, null, null, null, null, null);
        Assert.Equal("Wood", record.Name);
        Assert.Null(record.Recycle);
        Assert.Null(record.Decay);
        Assert.Null(record.Upkeep);
    }

    [Fact]
    public void ItemRecord_CarriesDecayAndUpkeep()
    {
        var record = new ItemRecord(
            2, "Wooden Door", 1, null, null, null, null,
            new DecayInfo(900, null, null, null, 100),
            new UpkeepCost([new UpkeepEntry(-151838493, 8, 25)]));

        Assert.Equal(900, record.Decay!.Seconds);
        Assert.Equal(100, record.Decay.Hp);
        Assert.Equal(-151838493, record.Upkeep!.Entries[0].ItemId);
        Assert.Equal(8, record.Upkeep.Entries[0].QuantityMin);
        Assert.Equal(25, record.Upkeep.Entries[0].QuantityMax);
    }
}
