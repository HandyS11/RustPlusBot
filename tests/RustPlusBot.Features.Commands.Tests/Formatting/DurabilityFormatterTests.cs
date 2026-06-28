using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DurabilityFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void DurabilityLine_SortsBySulfurAscending_AndAnnotatesSide()
    {
        var target = new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
        [
            new RaidCost(1, "soft", null, 4, 18, 5600, 120),
            new RaidCost(2, null, null, 2, 11.5, 4400, 120),
        ]);

        var line = DurabilityLine.Format(target, _names);

        Assert.Contains("Stone Wall", line, StringComparison.Ordinal);
        Assert.True(
            line.IndexOf("4400", StringComparison.Ordinal) < line.IndexOf("5600", StringComparison.Ordinal),
            "cheaper (4400 sulfur) cost should sort before 5600");
        Assert.Contains("(soft)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DurabilityLine_NullSulfur_SortsLast_AndOmitsSulfur()
    {
        var target = new RaidTarget("X", "X", RaidTargetKind.Item,
        [
            new RaidCost(1, null, null, 1, null, null, 50),  // no sulfur → sorts last
            new RaidCost(2, null, null, 1, 10, 1400, 30),
        ]);

        var line = DurabilityLine.Format(target, _names);

        // tool id 2 (1400 sulfur) appears before tool id 1 (no sulfur)
        Assert.True(line.IndexOf("Item 2", StringComparison.Ordinal) < line.IndexOf("Item 1", StringComparison.Ordinal));
    }

    [Fact]
    public void DurabilityLine_RoundsQuantityUp_AndShowsCaption()
    {
        var target = new RaidTarget("X", "X", RaidTargetKind.Item,
            [new RaidCost(1, null, "Semi-Automatic Rifle", 172.5, 70, 4325, null)]);

        var line = DurabilityLine.Format(target, _names);

        Assert.Contains("×173", line, StringComparison.Ordinal); // 172.5 rounded up
        Assert.Contains("Semi-Automatic Rifle", line, StringComparison.Ordinal);
    }
}
