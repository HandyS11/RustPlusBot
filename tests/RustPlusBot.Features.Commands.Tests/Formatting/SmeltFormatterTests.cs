using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class SmeltFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void SmeltLine_RendersHeader_Arrow_FuelAndTime()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 1, 1, 1.67, 3.33)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.StartsWith("Furnace:", line, StringComparison.Ordinal);
        Assert.Contains("→", line, StringComparison.Ordinal);
        Assert.Contains("1.67 wood", line, StringComparison.Ordinal);
        Assert.Contains("3.3s", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_ShowsQuantityPrefix_WhenAboveOne()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 15, 1, 5, 10)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.Contains("15× ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_OmitsQuantityPrefix_WhenOne()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 1, 1, 5, 10)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.DoesNotContain("1× ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_ShowsProbability_WhenBelowOne()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 1, 0.75, 1, 2)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.Contains("(75%)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_ShowsNoFuel_WhenWoodZero()
    {
        var smelter = new Smelter("100", "Electric Furnace", [new SmeltConversion(1, 2, 1, 1, 0, 2)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.Contains("no fuel", line, StringComparison.Ordinal);
        Assert.DoesNotContain("wood", line, StringComparison.Ordinal);
    }
}
