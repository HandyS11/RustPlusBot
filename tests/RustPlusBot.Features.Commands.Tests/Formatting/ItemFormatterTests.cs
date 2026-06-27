using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class ItemFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void ItemLine_IncludesNameAndStack()
    {
        var item = new ItemRecord(1, "AK-47", 1, 3600, null, null, null, null, null);
        var line = ItemLine.Format(item);
        Assert.Contains("AK-47", line, StringComparison.Ordinal);
        Assert.Contains("1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RecycleLine_ListsYields()
    {
        var item = new ItemRecord(1, "AK-47", 1, null,
            new RecycleYield([new YieldEntry(-1059362949, 4, 1.0)]), null, null, null, null);
        var line = RecycleLine.Format(item, _names);
        Assert.Contains("AK-47", line, StringComparison.Ordinal);
        Assert.Contains("4", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CraftLine_ListsIngredients()
    {
        var item = new ItemRecord(1, "AK-47", 1, null, null,
            new CraftRecipe([new Ingredient(-1059362949, 200)], 30.0, 3), null, null, null);
        var line = CraftLine.Format(item, _names);
        Assert.Contains("200", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchLine_ShowsScrap()
    {
        var item = new ItemRecord(1, "AK-47", 1, null, null, null, new ResearchCost(500), null, null);
        var line = ResearchLine.Format(item);
        Assert.Contains("500", line, StringComparison.Ordinal);
    }
}
